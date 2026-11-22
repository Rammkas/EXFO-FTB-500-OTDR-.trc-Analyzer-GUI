// ==============================================================================
// RawTrace.cs — экспорт СЫРОЙ КРИВОЙ трассы (поточные значения сигнала) из
// blobs RawSamples типа BinaryRef в таблицу CSV.
//
// ФАКТЫ О ФОРМАТЕ (проверено на эталонных .trc, напр. 9076.trc):
//   * каждый контейнер TraceN содержит потомка BinaryRef с именем RawSamples;
//   * blob расположен в РАСПАКОВАННОМ потоке сразу после заголовка узла:
//     данные начинаются с head + NodeHeaderSize и занимают ValLen байт;
//   * отсчёты — Int16 little-endian, ValLen чётный и ValLen/2 = число отсчётов
//     (9076.trc: 125 364 Б = 62 682 отсчёта на длину волны);
//   * шкала значений — ScaleFactor отсчётов на дБ (поле дерева ScaleFactor =
//     1024). Проверено: при 1024 отсчётах/дБ наклон распакованной трассы
//     совпадает с затуханиями EventTable/GOLD (0,32 дБ/км при 1310,
//     0,18 дБ/км при 1550);
//   * ось отсчётов равномерна по ВРЕМЕНИ: отсчёт i соответствует
//     t_i = RangeStart + i * SamplingPeriod секунд (SamplingPeriod = 3,125e-9 с,
//     т.е. 320 МОтс/с). Метры: distance_i = t_i * Range / (RangeEnd - RangeStart).
//     Сверено с позициями EventTable: пик отражения QC-коннектора попадает на
//     1014 м (EventTable 1010,9 м), пик конца волокна — на 8348 м
//     (EventTable 8349,3 м) — небольшое расхождение нормально: EXFO уточняет
//     позиции событий сверх сырой сетки отсчётов.
//
// Привязка blob -> трасса выполняется поиском узла RawSamples в таблице потомков
// его родительского контейнера TraceN, после чего читаются соседние скалярные
// узлы этого контейнера (Wavelength, Range, SamplingPeriod, RangeStart,
// RangeEnd, ScaleFactor).
//
// Модуль только выводит данные; поведение разбора и вердиктов не меняет.
//
// ЗАПУСК:  exfo_trc_analyzer.exe --raw-trace <файл.trc | папка>
//          (CSV пишется как прочие отчёты: папку выбирает --source_output)
// ==============================================================================
using System.Globalization;
using System.IO; // в WPF-проекте (net10.0-windows) implicit usings не включает System.IO

namespace ExfoTrcAnalyzer;

public static class RawTrace
{
    // Отсчёты — Int16 little-endian (проверенный факт, см. заголовок файла)
    private const int BytesPerSample = 2;

    // Запасная шкала дБ, если поле ScaleFactor отсутствует в дереве
    // (1024 отсчётов на дБ — проверенное значение в изученных файлах EXFO)
    private const int DefaultCountsPerDb = 1024;

    // Blobs RawSamples меньше этого размера считаются не-трассой (защита от
    // ложных заголовков узлов; реальные трассы — десятки тысяч отсчётов)
    private const int MinTraceBytes = 1024;

    public sealed class TraceCurve
    {
        public string TraceName = "";
        public int WavelengthNm;                 // 0 = неизвестно
        public short[] Samples = Array.Empty<short>();
        public int CountsPerDb = DefaultCountsPerDb;
        public double SamplingPeriodS;           // 0 = неизвестно
        public double RangeStartS;
        public double RangeEndS;
        public double RangeM;                    // 0 = неизвестно

        public int SampleCount => Samples.Length;

        public double TimeS(int i) => RangeStartS + i * SamplingPeriodS;

        // Метры вдоль волокна; NaN, если у трассы нет полей Range/SamplingPeriod
        public double DistanceM(int i)
        {
            if (RangeM <= 0 || SamplingPeriodS <= 0 || RangeEndS <= RangeStartS)
                return double.NaN;
            return TimeS(i) * RangeM / (RangeEndS - RangeStartS);
        }

        public double LevelDb(int i) => Samples[i] / (double)CountsPerDb;
    }

    // --------------------------------------------------------------------------
    // Прочитать все кривые RawSamples файла .trc
    // --------------------------------------------------------------------------
    public static List<TraceCurve> ReadCurves(string filepath)
    {
        byte[] merged = TrcParser.DecompressTrc(filepath);
        var curves = new List<TraceCurve>();

        foreach (var head in FindRawSampleNodes(merged))
        {
            int parent = FindParentContainer(merged, head.Offset);
            var scalars = parent >= 0 ? ReadChildScalars(merged, parent) : new Dictionary<string, TrcParser.NodeHeaderInfo>();

            var curve = new TraceCurve
            {
                TraceName = parent >= 0 ? PathOf(parent, merged) : "unknown",
                WavelengthNm = WavelengthNm(merged, scalars),
                CountsPerDb = (int)(GetDouble(merged, scalars, "ScaleFactor") ?? DefaultCountsPerDb),
                SamplingPeriodS = GetDouble(merged, scalars, "SamplingPeriod") ?? 0,
                RangeStartS = GetDouble(merged, scalars, "RangeStart") ?? 0,
                RangeEndS = GetDouble(merged, scalars, "RangeEnd") ?? 0,
                RangeM = GetDouble(merged, scalars, "Range") ?? 0,
            };

            int dataStart = head.Offset + TrcParser.NodeHeaderSize;
            int count = Math.Min(head.ValLen, merged.Length - dataStart) / BytesPerSample;
            curve.Samples = new short[count];
            for (int i = 0; i < count; i++)
                curve.Samples[i] = BitConverter.ToInt16(merged, dataStart + i * BytesPerSample);
            curves.Add(curve);
        }
        return curves;
    }

    // --------------------------------------------------------------------------
    // Сохранить CSV всех кривых (стиль проекта: Csv.SaveCsv — разделитель ';',
    // запятая как десятичный разделитель, UTF-8 с BOM)
    // --------------------------------------------------------------------------
    public static void SaveCsv(string csvFilepath, List<TraceCurve> curves)
    {
        var headers = new[] { "Трасса", "Длина волны (нм)", "№ отсчёта", "Время (с)", "Расстояние (м)", "Отсчёт Int16", "Уровень (дБ)" };
        var rows = new List<string[]>();

        foreach (var c in curves)
            for (int i = 0; i < c.SampleCount; i++)
                rows.Add(new[]
                {
                    c.TraceName,
                    c.WavelengthNm > 0 ? c.WavelengthNm.ToString(CultureInfo.InvariantCulture) : "",
                    i.ToString(CultureInfo.InvariantCulture),
                    Csv.Fmt(c.SamplingPeriodS > 0 ? c.TimeS(i) : null, 12),
                    Csv.Fmt(double.IsNaN(c.DistanceM(i)) ? null : c.DistanceM(i), 3),
                    c.Samples[i].ToString(CultureInfo.InvariantCulture),
                    Csv.Fmt(c.LevelDb(i), 3),
                });

        Csv.SaveCsv(csvFilepath, headers, rows);
    }

    // --------------------------------------------------------------------------
    // Поиск blobs
    // --------------------------------------------------------------------------
    // Найти узлы BinaryRef с именем "RawSamples": строка имени ищется в потоке,
    // затем ищется заголовок узла, поле nameOffset которого указывает на неё
    // (тот же приём «иглы», что у TrcParser.FindRootTree).
    private static List<TrcParser.NodeHeaderInfo> FindRawSampleNodes(byte[] merged)
    {
        var found = new List<TrcParser.NodeHeaderInfo>();
        int nameSearch = 0;
        while (nameSearch < merged.Length)
        {
            int rel = merged.AsSpan(nameSearch).IndexOf("RawSamples\0"u8);
            if (rel < 0)
                break;
            int namePos = nameSearch + rel;

            byte[] needle = BitConverter.GetBytes(namePos);
            int needleSearch = 0;
            while (needleSearch < merged.Length)
            {
                int r = merged.AsSpan(needleSearch).IndexOf(needle);
                if (r < 0)
                    break;
                int pos = needleSearch + r;
                var head = TrcParser.ReadNodeHeader(merged, pos);
                if (head?.Name == "RawSamples" &&
                    head.Type == (int)TrcParser.TrcNodeType.BinaryRef &&
                    head.ValLen >= MinTraceBytes &&
                    head.Offset + TrcParser.NodeHeaderSize + MinTraceBytes <= merged.Length)
                    found.Add(head);
                needleSearch = pos + 1;
            }
            nameSearch = namePos + 1;
        }
        return found;
    }

    // Найти контейнер, в таблице потомков которого лежит данное смещение потомка:
    // 4-байтовая ссылка на потомка должна стоять по адресу
    // head + NodeHeaderSize + j*ChildRefSize внутри корректного узла Container.
    private static int FindParentContainer(byte[] merged, int childOffset)
    {
        byte[] needle = BitConverter.GetBytes(childOffset);
        int search = 0;
        while (search < merged.Length)
        {
            int r = merged.AsSpan(search).IndexOf(needle);
            if (r < 0)
                break;
            int slot = search + r; // позиция ссылки на потомка в какой-то таблице потомков

            for (int j = 0; ; j++)
            {
                int head = slot - TrcParser.NodeHeaderSize - j * TrcParser.ChildRefSize;
                if (head <= 0)
                    break;
                var h = TrcParser.ReadNodeHeader(merged, head);
                if (h == null || h.Type != (int)TrcParser.TrcNodeType.Container)
                    continue;
                int valStart = head + TrcParser.NodeHeaderSize;
                int childCount = Math.Max(0, Math.Min(h.ValLen, merged.Length - valStart)) / TrcParser.ChildRefSize;
                if (j < childCount && BitConverter.ToInt32(merged, valStart + j * TrcParser.ChildRefSize) == childOffset)
                    return head;
            }
            search = slot + 1;
        }
        return -1;
    }

    // Скалярные/дочерние узлы контейнера: имя -> информация о заголовке
    private static Dictionary<string, TrcParser.NodeHeaderInfo> ReadChildScalars(byte[] merged, int containerOffset)
    {
        var result = new Dictionary<string, TrcParser.NodeHeaderInfo>();
        var h = TrcParser.ReadNodeHeader(merged, containerOffset);
        if (h == null || h.Type != (int)TrcParser.TrcNodeType.Container)
            return result;

        int valStart = containerOffset + TrcParser.NodeHeaderSize;
        int childCount = Math.Max(0, Math.Min(h.ValLen, merged.Length - valStart)) / TrcParser.ChildRefSize;
        for (int i = 0; i < childCount; i++)
        {
            int cOff = BitConverter.ToInt32(merged, valStart + i * TrcParser.ChildRefSize);
            if (cOff == TrcParser.NoChildRef)
                continue;
            var ch = TrcParser.ReadNodeHeader(merged, cOff);
            if (ch != null && !result.ContainsKey(ch.Name))
                result[ch.Name] = ch;
        }
        return result;
    }

    // Имя контейнера (Trace0, Trace1, ...) — используется как метка трассы
    private static string PathOf(int containerOffset, byte[] merged)
        => TrcParser.ReadNodeHeader(merged, containerOffset)?.Name ?? "unknown";

    private static int WavelengthNm(byte[] merged, Dictionary<string, TrcParser.NodeHeaderInfo> scalars)
    {
        double? w = GetDouble(merged, scalars, "Wavelength"); // хранится в метрах, напр. 1,31e-6
        return w is null or <= 0 ? 0 : (int)Math.Round(w.Value * 1e9);
    }

    // Прочитать скалярное значение Double (отсутствует/повреждено -> null)
    private static double? GetDouble(byte[] merged, Dictionary<string, TrcParser.NodeHeaderInfo> scalars, string name)
    {
        if (!scalars.TryGetValue(name, out var ch) || ch.Type != (int)TrcParser.TrcNodeType.Double)
            return null;
        int valStart = ch.Offset + TrcParser.NodeHeaderSize;
        if (ch.ValLen != sizeof(double) || valStart + sizeof(double) > merged.Length)
            return null;
        double v = BitConverter.ToDouble(merged, valStart);
        return double.IsNaN(v) ? null : v; // EXFO кодирует «нет значения» как NaN
    }
}
