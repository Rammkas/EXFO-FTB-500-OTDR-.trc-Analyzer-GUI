// ==============================================================================
// Csv.cs — форматирование и запись CSV-таблиц результатов.
//
// СОГЛАШЕНИЯ ВЫВОДА (под русскую локаль Excel):
// - разделитель полей — ';' (русский Excel по умолчанию);
// - десятичный разделитель в числах — запятая (форматируем InvariantCulture
//   с заменой '.' на ',');
// - кодировка — UTF-8 с BOM, чтобы Excel/Блокнот корректно показали кириллицу
//   шапок и причин отбраковки;
// - поля, содержащие ';' '"' перевод строки, берутся в кавычки, внутренние
//   кавычки удваиваются (стандартное CSV-экранирование);
// - пустое числовое значение (null) пишется как пустое поле, а не "0" или
//   "NaN" — отсутствие данных в рефлектограмме не должно выглядеть как ноль.
// ==============================================================================
using System.Globalization;
using System.IO; // в WPF-проекте (net10.0-windows) implicit usings не включает System.IO
using System.Text;

namespace ExfoTrcAnalyzer;

// ==============================================================================
// Форматирование и сохранение CSV (разделитель ';', запятая как десятичная,
// UTF-8 с BOM — для корректного открытия в Excel)
// ==============================================================================
public static class Csv
{
    public static string Fmt(double? val, int digits = 3)
    {
        if (val == null)
            return "";
        return val.Value.ToString("F" + digits, CultureInfo.InvariantCulture).Replace('.', ',');
    }

    public static string CsvField(string field)
    {
        if (field.Contains(';') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        return field;
    }

    public static void SaveCsv(string filename, string[] headers, List<string[]> rows)
    {
        using var fs = File.Create(filename);
        using var w = new StreamWriter(fs, new UTF8Encoding(true)); // utf-8-sig (с BOM)
        w.WriteLine(string.Join(";", headers.Select(CsvField)));
        foreach (var row in rows)
            w.WriteLine(string.Join(";", row.Select(CsvField)));
        w.Flush();
    }
}
