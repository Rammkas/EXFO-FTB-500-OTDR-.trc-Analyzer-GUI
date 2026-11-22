// ==============================================================================
// Config.cs — настройки стенда ОТК и порогов отбраковки (PASS/FAIL).
//
// ЧТО ДЕЛАЕТ: держит значения по умолчанию и умеет переопределять их файлом
// otdr_config.txt. Формат файла: "параметр = значение", строки с # ; // —
// комментарии; числа принимаются и с точкой, и с запятой; bool — True/False/
// 1/0/yes/no/да/нет и т.п. Файл ищется сначала в папке с .trc, затем рядом с EXE.
// Если файла нет — встроенные значения по умолчанию, и в папке с .trc создаётся
// файл-шаблон (ConfigTemplate) для ручного редактирования в Блокноте.
//
// СМЫСЛ ПАРАМЕТРОВ (значения по умолчанию — под стенд: буфер ~1 км, SMF-волокно):
//   has_buffer_spool       — первый участок трассы (~1 км) является буферной
//                            катушкой стенда и не входит в проверяемое волокно.
//                            ЗАМЕР БЕЗ БУФЕРА (волокно напрямую к постоянному
//                            коннектору): обязательно False — тогда вся трасса
//                            считается проверяемым волокном, колонки QC-коннектора
//                            пустые. Если оставить True при прямом замере волокна
//                            длиной 800..1200 м — оно будет молча принято за буфер
//                            и исключено из проверки (в отчёте: длина 0 м, PASS при
//                            пустых данных). Analyzer печатает предупреждение в таких
//                            несоответствиях; по самой трассе «безбуферность» не
//                            определяется — это только ответственность настройки.
//   buffer_min_m/max_m     — окно длины буфера для его распознавания (800..1200 м;
//                            фактические буфера в эталонных файлах ~1010 м);
//   atten_1310_max         — норма затухания проверяемого волокна на 1310 нм,
//                            дБ/км (0.36; типичное измеренное на эталонах 0.32-0.33);
//   atten_1550_max         — то же для 1550 нм, дБ/км (0.22; измеренное ~0.18);
//   splice_loss_max        — норма потерь внутренних сварок бухты, дБ (0.30);
//   check_qc_connector     — проверять ли потери входного разъема ОТК (по умолчанию
//                            False: только выводится в отчёт; при True — влияет
//                            на вердикт);
//   qc_connector_loss_max  — порог потерь QC-коннектора, дБ (0.50; измеренные
//                            значения на эталонах 0.13..0.54 — порог надо
//                            согласовать с нормой изделия!);
//   match_tolerance_m      — допуск сопоставления одноименных событий/участков
//                            на 1310 и 1550 нм по координате, м (5.0).
//
// Config — статический класс (приложение однопоточное). ResetDefaults() нужен
// тестам, чтобы не наследовать значения предыдущего теста.
// ==============================================================================
using System.Globalization;
using System.IO; // в WPF-проекте (net10.0-windows) implicit usings не включает System.IO
using System.Text;

namespace ExfoTrcAnalyzer;

// ==============================================================================
// НАСТРОЙКИ СТЕНДА ОТК И ПОРОГОВ ОТБРАКОВКИ (PASS / FAIL)
// Переопределяются файлом otdr_config.txt (в папке с .trc или рядом с EXE)
// ==============================================================================
public static class Config
{
    public static bool HasBufferSpool = true;       // True = первый участок (~1 км) это буферная катушка
    public static double BufferMinM = 800.0;        // Мин. длина буферной катушки (м)
    public static double BufferMaxM = 1200.0;       // Макс. длина буферной катушки (м)
    public static double Atten1310Max = 0.36;       // Макс. затухание проверяемого волокна 1310 нм (дБ/км)
    public static double Atten1550Max = 0.22;       // Макс. затухание проверяемого волокна 1550 нм (дБ/км)
    public static double SpliceLossMax = 0.30;      // Макс. потери на внутренних сварках внутри бухты (дБ)
    public static bool CheckQcConnector = false;    // Проверять ли входной разъем ОТК на брак
    public static double QcConnectorLossMax = 0.50; // Макс. потери входного разъема ОТК (дБ), см. check_qc_connector
    public static double MatchToleranceM = 5.0;     // Допуск сопоставления событий/участков 1310/1550 (м)

    public const string ConfigFileName = "otdr_config.txt";

    public const string ConfigTemplate =
        "# ================================================================================\r\n" +
        "# НАСТРОЙКИ АНАЛИЗАТОРА РЕФЛЕКТОГРАММ EXFO (.trc) — АНАЛОГ БЛОКА CONFIG\r\n" +
        "# Файл читается программой автоматически. Формат: параметр = значение.\r\n" +
        "# Строки, начинающиеся с # — комментарии. Значения можно менять в Блокноте.\r\n" +
        "# Если удалить этот файл — будут использованы встроенные значения по умолчанию.\r\n" +
        "# ================================================================================\r\n" +
        "\r\n" +
        "# Первый участок (~1 км) — буферная катушка? (True / False)\r\n" +
        "has_buffer_spool = True\r\n" +
        "\r\n" +
        "# Мин. и макс. длина буферной катушки (м)\r\n" +
        "buffer_min_m = 800.0\r\n" +
        "buffer_max_m = 1200.0\r\n" +
        "\r\n" +
        "# Макс. затухание проверяемого волокна (дБ/км)\r\n" +
        "atten_1310_max = 0.36\r\n" +
        "atten_1550_max = 0.22\r\n" +
        "\r\n" +
        "# Макс. потери на внутренних сварках внутри бухты (дБ)\r\n" +
        "splice_loss_max = 0.30\r\n" +
        "\r\n" +
        "# Проверять ли входной разъем ОТК (Event 2) на брак (True / False)\r\n" +
        "check_qc_connector = False\r\n" +
        "\r\n" +
        "# Макс. потери входного разъема ОТК (дБ), действует при check_qc_connector = True\r\n" +
        "qc_connector_loss_max = 0.50\r\n" +
        "\r\n" +
        "# Допуск сопоставления событий и участков 1310/1550 нм по координате (м)\r\n" +
        "match_tolerance_m = 5.0\r\n";

    public static void ResetDefaults()
    {
        HasBufferSpool = true;
        BufferMinM = 800.0;
        BufferMaxM = 1200.0;
        Atten1310Max = 0.36;
        Atten1550Max = 0.22;
        SpliceLossMax = 0.30;
        CheckQcConnector = false;
        QcConnectorLossMax = 0.50;
        MatchToleranceM = 5.0;
    }

    // --------------------------------------------------------------------------
    // Чтение настроек из otdr_config.txt. Ищется в папке с .trc-файлами, затем
    // рядом с EXE. Если файла нет — значения по умолчанию, файл-шаблон создается.
    // --------------------------------------------------------------------------
    public static string? Load(string inputDir, Action<string>? log = null)
    {
        log ??= Console.WriteLine;

        string[] candidates =
        {
            Path.Combine(inputDir, ConfigFileName),
            Path.Combine(AppContext.BaseDirectory, ConfigFileName),
        };

        string? configPath = candidates.FirstOrDefault(File.Exists);
        if (configPath == null)
        {
            log($"[CONFIG] Файл {ConfigFileName} не найден — используются встроенные значения по умолчанию.");
            TryCreateConfigTemplate(inputDir, log);
            return null;
        }

        int applied = 0;
        foreach (string rawLine in File.ReadLines(configPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';') || line.StartsWith("//"))
                continue;

            int sep = line.IndexOfAny(new[] { '=', ':' });
            if (sep < 0)
            {
                log($"[CONFIG] Пропущена строка (нет '=' ): {line}");
                continue;
            }

            string key = line.Substring(0, sep).Trim().ToLowerInvariant();
            string val = line.Substring(sep + 1).Trim();

            switch (key)
            {
                case "has_buffer_spool":
                    ApplyBool(key, val, ref HasBufferSpool, ref applied, log);
                    break;
                case "buffer_min_m":
                    ApplyDouble(key, val, ref BufferMinM, ref applied, log);
                    break;
                case "buffer_max_m":
                    ApplyDouble(key, val, ref BufferMaxM, ref applied, log);
                    break;
                case "atten_1310_max":
                    ApplyDouble(key, val, ref Atten1310Max, ref applied, log);
                    break;
                case "atten_1550_max":
                    ApplyDouble(key, val, ref Atten1550Max, ref applied, log);
                    break;
                case "splice_loss_max":
                    ApplyDouble(key, val, ref SpliceLossMax, ref applied, log);
                    break;
                case "check_qc_connector":
                    ApplyBool(key, val, ref CheckQcConnector, ref applied, log);
                    break;
                case "qc_connector_loss_max":
                    ApplyDouble(key, val, ref QcConnectorLossMax, ref applied, log);
                    break;
                case "match_tolerance_m":
                    ApplyDouble(key, val, ref MatchToleranceM, ref applied, log);
                    break;
                default:
                    log($"[CONFIG] Неизвестный параметр: {key}");
                    break;
            }
        }

        log($"[CONFIG] Загружен '{Path.GetFileName(configPath)}' (применено параметров: {applied})");
        return configPath;
    }

    public static bool ParseBool(string val, out bool result)
    {
        switch (val.ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "y":
            case "on":
            case "да":
                result = true;
                return true;
            case "false":
            case "0":
            case "no":
            case "n":
            case "off":
            case "нет":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }

    public static bool ParseDouble(string val, out double result)
        => double.TryParse(val.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static void ApplyBool(string key, string val, ref bool target, ref int applied, Action<string> log)
    {
        if (ParseBool(val, out bool parsed))
        {
            target = parsed;
            applied++;
        }
        else
        {
            log($"[CONFIG] Неверное значение '{val}' для {key} (ожидается True/False) — оставлено {target}.");
        }
    }

    private static void ApplyDouble(string key, string val, ref double target, ref int applied, Action<string> log)
    {
        if (ParseDouble(val, out double parsed))
        {
            target = parsed;
            applied++;
        }
        else
        {
            log($"[CONFIG] Неверное значение '{val}' для {key} (ожидается число) — оставлено {target}.");
        }
    }

    private static void TryCreateConfigTemplate(string inputDir, Action<string> log)
    {
        try
        {
            string target = Path.Combine(inputDir, ConfigFileName);
            File.WriteAllText(target, ConfigTemplate, new UTF8Encoding(true));
            log($"[CONFIG] Создан файл настроек '{target}' — его значения можно менять в Блокноте.");
        }
        catch
        {
            // если записать нельзя (например, нет прав) — работаем по умолчанию
        }
    }
}
