// ==============================================================================
// TrcParser.cs — низкоуровневый разбор бинарного формата EXFO .trc.
//
// ФОРМАТ .trc (реверс-инжиниринг, подтверждён на 4 эталонных файлах FTB-500):
//
// 1) Файл представляет собой «кашу»: служебные бинарные данные + последовательность
//    zlib-сжатых чанков. Парсинг: посимвольный скан в поисках заголовка zlib
//    (CMF/FLG), попытка декомпрессии от каждой кандидатуры, удачные чанки
//    concaten'ятся в один распакованный поток.
//    zlib (RFC 1950): CMF=0x78 (deflate, окно 32K), FLG выбран так, что
//    (CMF*256+FLG) % 31 == 0. На всех проверенных .trc EXFO FTB-500 пишет ровно
//    9 чанков, все с FLG=0xDA (уровень сжатия 9, максимальный).
//    ВАЖНО: ZLibStream в .NET читает underlying-поток с read-ahead буфером,
//    поэтому его Position НЕ равен концу zlib-потока и «перепрыгивать» распа-
//    кованный чанок по Position нельзя (проверено: на .NET 8 это теряет все
//    чанки после первого).
//
// 2) Распакованный поток — дерево узлов. Каждый узел: заголовок 16 байт
//    [nameOffset:int32][dataType:int32][valLen:int32][selfOffset:int32] + значение
//    сразу после заголовка. selfOffset (поле [12..16)) == offset+16 у всех 640
//    проверенных узлов; при разборе не используется. Смещения в дереве АБСОЛЮТНЫЕ
//    (относительные начала распакованного потока).
//
// 3) dataType (подтверждено подсчётом узлов на 9076.trc):
//    0=Container (39 узлов: таблица int32-ссылок на детей), 1=Int32 (120),
//    2=BinaryRef (40: CLSID-GUID 16 байт, RawSamples — пропускаем),
//    3=Double (362; NaN = «нет значения», напр. Loss входного коннектора),
//    4=Utf16String (79), 5=Bool8 (0 узлов — тип унаследован из Python-парсера).
//
// 4) Корень дерева ищется так: в потоке находится ASCII-строка "OtdrFile\0",
//    затем ищется её 4-байтное абсолютное смещение — это поле nameOffset заголовка
//    корневого узла. Первое совпадение может быть случайным в бинарных данных,
//    поэтому каждая кандидатура проверяется: узел читается и имеет имя "OtdrFile".
//
// ЗАЩИТЫ: имена узлов <= 256 байт; обход дерева с visited-множеством и глубиной
// <= 200 (циклические ссылки в повреждённом файле не вызовут StackOverflow);
// строки UTF-16 с нечётной длиной обрезаются; valLen<0 отклоняется.
// ==============================================================================
using System.Globalization;
using System.IO; // в WPF-проекте (net10.0-windows) implicit usings не включает System.IO
using System.IO.Compression;
using System.Text;

namespace ExfoTrcAnalyzer;

// ==============================================================================
// РАЗБОР ФОРМАТА EXFO .trc:
//   1) распаковка zlib-чанков;
//   2) чтение дерева узлов по абсолютным смещениям в распакованном потоке.
// Защита: имена узлов ограничены, обход дерева с контролем циклов и глубины,
// корневой узел проверяется по имени "OtdrFile".
// ==============================================================================
public static class TrcParser
{
    public const int MaxNameLength = 256;  // макс. длина имени узла (защита от мусорных смещений)
    public const int MaxTreeDepth = 200;   // макс. глубина вложенности дерева (защита от циклов)
    public const int MinChunkLength = 50;  // игнорировать слишком мелкие распакованные чанки

    // zlib (RFC 1950): поток начинается 2-байтным заголовком CMF/FLG.
    // CMF=0x78 — метод deflate, окно 32K. FLG выбирается так, что
    // (CMF*256 + FLG) % 31 == 0 (биты контроля + уровень сжатия).
    // Для CMF=0x78 это FLG: 0x01, 0x20, 0x3F, 0x5E, 0x7D, 0x9C, 0xBB, 0xDA, 0xF9.
    // На всех проверенных .trc EXFO FTB-500 чанки идут с FLG=0xDA (уровень 9).
    // Проверка — только быстрый префильтр перед попыткой декомпрессии.
    public const byte ZlibCmf = 0x78;

    public static bool IsZlibStreamStart(byte cmf, byte flg)
        => cmf == ZlibCmf && ((cmf << 8 | flg) % 31) == 0;

    // Заголовок узла дерева EXFO .trc — 16 байт (эмпирически подтверждено на реальном
    // файле: 640 узлов, layout одинаков у всех):
    //   [0..4)   — абсолютное смещение имени узла (ASCII-строка, terminator 0x00);
    //   [4..8)   — тип данных (см. TrcNodeType);
    //   [8..12)  — длина значения в байтах;
    //   [12..16) — абсолютное смещение области значения (всегда == offset + 16;
    //              при разборе не используется).
    // Само значение лежит сразу за заголовком, по offset + NodeHeaderSize.
    public const int NodeHeaderSize = 16;
    public const int ChildRefSize = 4; // размер ссылки на дочерний узел (int32)
    public const int NoChildRef = 0;   // 0 в таблице детей означает «ссылки нет»

    // Типы данных узлов. Нумерация унаследована из исходного Python-парсера и
    // проверена на реальном .trc (9076.trc): Container 39, Int32 120, BinaryRef 40,
    // Double 362, Utf16String 79 узлов.
    public enum TrcNodeType
    {
        Container   = 0, // контейнер: значение = таблица смещений детей (int32 x N, N = valLen/4)
        Int32       = 1, // int32 (Count, Type, Status, CursorA, EventChanged...)
        BinaryRef   = 2, // бинарная ссылка: CLSID (GUID, 16 байт), RawSamples (сырая кривая трассы —
                         // экспортируется модулем RawTrace, для анализа не используется), в дереве отдаём null
        Double      = 3, // double (Position, Length, Loss, Reflectance...); NaN трактуется как «нет значения»
        Utf16String = 4, // строка UTF-16LE (Comment, Date, Identifier...); terminator '\0' обрезается
        Bool8       = 5, // bool 1 байт — на проверенных файлах не встречался, оставлен из Python-парсера
    }

    // --------------------------------------------------------------------------
    // Распаковка Zlib-чанков из EXFO .trc
    // --------------------------------------------------------------------------
    public static byte[] DecompressTrc(string filepath)
    {
        byte[] data = File.ReadAllBytes(filepath);
        using var merged = new MemoryStream();

        for (int offset = 0; offset + 1 < data.Length; offset++)
        {
            if (!IsZlibStreamStart(data[offset], data[offset + 1]))
                continue;

            try
            {
                using var inMs = new MemoryStream(data, offset, data.Length - offset, writable: false);
                using var zs = new ZLibStream(inMs, CompressionMode.Decompress);
                using var chunkMs = new MemoryStream();
                zs.CopyTo(chunkMs);
                byte[] chunk = chunkMs.ToArray();
                if (chunk.Length > MinChunkLength)
                    merged.Write(chunk, 0, chunk.Length);
            }
            catch
            {
                // не валидный zlib-поток — пропускаем
                // ВАЖНО: не пытаться "перепрыгнуть" распакованный поток через inMs.Position:
                // ZLibStream делает read-ahead буфером и Position не равен концу zlib-потока.
            }
        }

        return merged.ToArray();
    }

    // --------------------------------------------------------------------------
    // Публичный читатель 16-байтового заголовка узла (имя/тип/длина значения)
    // без разбора самого значения — используется модулями отображения (FileMap,
    // RawTrace). Применяет те же правила корректности, что и ReadNodeAt
    // (ограниченная длина имени, ASCII-имя с завершающим нулём, смещение
    // внутри буфера).
    // --------------------------------------------------------------------------
    public sealed record NodeHeaderInfo(int Offset, string Name, int Type, int ValLen);

    public static NodeHeaderInfo? ReadNodeHeader(byte[] buf, int offset)
    {
        if (offset <= 0 || offset + NodeHeaderSize > buf.Length)
            return null;

        int nameOffset = BitConverter.ToInt32(buf, offset);
        int type = BitConverter.ToInt32(buf, offset + 4);
        int valLen = BitConverter.ToInt32(buf, offset + 8);

        if (nameOffset < 0 || nameOffset >= buf.Length)
            return null;

        int window = Math.Min(MaxNameLength, buf.Length - nameOffset);
        int nullPos = Array.FindIndex(buf, nameOffset, window, b => b == 0);
        if (nullPos == -1)
            return null;

        return new NodeHeaderInfo(offset, DecodeAscii(buf, nameOffset, nullPos - nameOffset), type, valLen);
    }

    // --------------------------------------------------------------------------
    // Чтение узла дерева EXFO строго по смещению offset
    // --------------------------------------------------------------------------
    public static (string? Name, object? Value) ReadNodeAt(byte[] buf, int offset)
        => ReadNodeAt(buf, offset, new HashSet<int>(), 0);

    private static (string? Name, object? Value) ReadNodeAt(byte[] buf, int offset, HashSet<int> visited, int depth)
    {
        // offset 0 и за пределами буфера = «узла нет» (0 — пустая ссылка в таблицах детей)
        if (offset <= 0 || offset + NodeHeaderSize > buf.Length)
            return (null, null);

        // защита от циклических ссылок и чрезмерной глубины (иначе StackOverflow)
        if (depth > MaxTreeDepth || !visited.Add(offset))
            return (null, null);

        int nameOffset = BitConverter.ToInt32(buf, offset);
        int dataType = BitConverter.ToInt32(buf, offset + 4);
        int valLen = BitConverter.ToInt32(buf, offset + 8);

        if (nameOffset < 0 || nameOffset >= buf.Length)
            return (null, null);

        int nameWindow = Math.Min(MaxNameLength, buf.Length - nameOffset);
        int nullPos = Array.FindIndex(buf, nameOffset, nameWindow, b => b == 0);
        if (nullPos == -1)
            return (null, null); // имя слишком длинное или отсутствует — скорее всего, мусорное смещение

        string name = DecodeAscii(buf, nameOffset, nullPos - nameOffset);

        int valStart = offset + NodeHeaderSize;
        if (valStart >= buf.Length)
            return (name, null);
        if (valLen < 0)
            return (name, null); // поврежденная длина значения

        int actualLen = Math.Min(valLen, buf.Length - valStart);

        if (dataType == (int)TrcNodeType.Container)
        {
            int childCount = actualLen / ChildRefSize;
            var obj = new Dictionary<string, object?>();
            for (int i = 0; i < childCount; i++)
            {
                int cOff = BitConverter.ToInt32(buf, valStart + i * ChildRefSize);
                if (cOff != NoChildRef)
                {
                    var (cName, cVal) = ReadNodeAt(buf, cOff, visited, depth + 1);
                    if (!string.IsNullOrEmpty(cName))
                        obj[cName!] = cVal;
                }
            }
            return (name, obj);
        }

        if (dataType == (int)TrcNodeType.Int32 && actualLen == sizeof(int))
        {
            int val = BitConverter.ToInt32(buf, valStart);
            return (name, val);
        }

        if (dataType == (int)TrcNodeType.Double && actualLen == sizeof(double))
        {
            double val = BitConverter.ToDouble(buf, valStart);
            // EXFO кодирует «нет значения» как NaN (например Loss у коннектора на входе)
            return (name, double.IsNaN(val) ? null : val);
        }

        if (dataType == (int)TrcNodeType.Utf16String)
        {
            int len = actualLen & ~1; // UTF-16: только четная длина, иначе ArgumentException
            string s = len == 0 ? "" : Encoding.Unicode.GetString(buf, valStart, len).TrimEnd('\0');
            return (name, s);
        }

        if (dataType == (int)TrcNodeType.Bool8 && actualLen == 1)
            return (name, buf[valStart] != 0);

        // TrcNodeType.BinaryRef (CLSID, RawSamples) и неизвестные типы — значение нам не нужно
        return (name, null);
    }

    public static string DecodeAscii(byte[] buf, int start, int len)
    {
        var sb = new StringBuilder(len);
        for (int i = 0; i < len; i++)
        {
            byte b = buf[start + i];
            // 128 = граница диапазона ASCII; не-ASCII байты имени заменяем знаком замены
            sb.Append(b < 128 ? (char)b : '\uFFFD');
        }
        return sb.ToString();
    }

    // --------------------------------------------------------------------------
    // Поиск корневого узла: первое вхождение 4-байтной ссылки на "OtdrFile\0"
    // может быть случайным совпадением в бинарных данных, поэтому кандидат
    // проверяется: узел по этому смещению должен читаться и иметь имя "OtdrFile".
    // --------------------------------------------------------------------------
    public static Dictionary<string, object?> FindRootTree(byte[] stream)
    {
        int nameSearch = 0;
        while (nameSearch < stream.Length)
        {
            int relNamePos = stream.AsSpan(nameSearch).IndexOf("OtdrFile\0"u8);
            if (relNamePos < 0)
                break;
            int namePos = nameSearch + relNamePos;

            byte[] needle = BitConverter.GetBytes(namePos);
            int needleSearch = 0;
            while (needleSearch < stream.Length)
            {
                int relPos = stream.AsSpan(needleSearch).IndexOf(needle);
                if (relPos < 0)
                    break;
                int pos = needleSearch + relPos;

                var (nodeName, treeObj) = ReadNodeAt(stream, pos);
                if (nodeName == "OtdrFile" && treeObj is Dictionary<string, object?> tree)
                    return tree; // подтвержденный корень

                needleSearch = pos + 1;
            }

            nameSearch = namePos + 1;
        }

        throw new InvalidDataException(ExfoTrcAnalyzerGUI.Loc.T("err_trc_root"));
    }

    // --------------------------------------------------------------------------
    // Помощники обхода дерева
    // --------------------------------------------------------------------------
    public static object? Get(Dictionary<string, object?> obj, string key)
        => obj.TryGetValue(key, out var v) ? v : null;

    public static Dictionary<string, object?> AsDict(object? v)
        => v as Dictionary<string, object?> ?? new Dictionary<string, object?>();

    public static double? AsDouble(object? v) => v switch
    {
        double d => d,
        int i => i,
        _ => null,
    };

    public static string ValueToString(object? v) => v switch
    {
        string s => s,
        int i => i.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("G17", CultureInfo.InvariantCulture),
        _ => "",
    };
}
