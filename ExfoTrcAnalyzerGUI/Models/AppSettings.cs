// ==============================================================================
// AppSettings.cs — настройки анализа в памяти GUI (аналог otdr_config.txt).
// Значения по умолчанию совпадают с Config.cs консольной версии.
// Окно «Настройки» читает/сохраняет этот класс; позже к нему подключится
// движок анализа и файл otdr_config.txt.
// ==============================================================================
using System.IO; // в WPF-проекте implicit usings не включает System.IO

namespace ExfoTrcAnalyzerGUI;

public class AppSettings
{
    public bool HasBufferSpool { get; set; } = true;
    public double BufferMinM { get; set; } = 800.0;
    public double BufferMaxM { get; set; } = 1200.0;
    public double Atten1310Max { get; set; } = 0.36;
    public double Atten1550Max { get; set; } = 0.22;
    public double SpliceLossMax { get; set; } = 0.30;
    public bool CheckQcConnector { get; set; } = false;
    public double QcConnectorLossMax { get; set; } = 0.50;
    public double MatchToleranceM { get; set; } = 5.0;
    // «Жёлтая зона»: затухание в пределах ± этого % от порога считает результат
    // пограничным (жёлтый кружок), даже если вердикт PASS
    public double BorderlinePercent { get; set; } = 5.0;
    // Показывать столбцы «Потери» (дБ) во вкладках «Волокна»/«Участки»/«События»
    public bool ShowLosses { get; set; } = false; // по умолчанию скрыты
    // Режим сравнения: курсоры А/В не сбрасываются при переключении волокна/файла
    public bool CompareMode { get; set; } = false;

    // Порядок цветов модулей: позиция = номер модуля (1..12), значение = номер
    // стандартного цвета TIA-598 (1..12). По умолчанию — стандартный порядок;
    // переопределяется в окне настроек и сохраняется на диске.
    public int[] ModuleColorOrder { get; set; } = ModuleColors.Default();

    // Номер стандартного цвета для модуля (цикл 12, запасной — стандартный)
    public int ColorForModule(int moduleNo)
    {
        int i = (moduleNo - 1) % 12;
        int c = i < ModuleColorOrder.Length ? ModuleColorOrder[i] : i + 1;
        return c is >= 1 and <= 12 ? c : i + 1;
    }

    // Единый экземпляр на время сессии (окно настроек заменяет его при сохранении)
    public static AppSettings Current { get; set; } = LoadWithSavedColors();

    private static AppSettings LoadWithSavedColors()
        => new() { ModuleColorOrder = ModuleColors.Load() };

    public static void ResetToDefaults() => Current = new();
}

// Порядок цветов модулей хранится в %LOCALAPPDATA%\ExfoTrcAnalyzerGUI\module_colors.txt
// (12 чисел через запятую; повреждённый/отсутствующий файл — стандартный порядок)
public static class ModuleColors
{
    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExfoTrcAnalyzerGUI", "module_colors.txt");

    public static int[] Default() => [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

    public static int[] Load()
    {
        try
        {
            var parts = File.ReadAllText(StorePath)
                .Split([',', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 12)
                return Default();
            var order = new int[12];
            for (int i = 0; i < 12; i++)
            {
                order[i] = int.Parse(parts[i]);
                if (order[i] < 1 || order[i] > 12)
                    return Default();
            }
            return order;
        }
        catch
        {
            return Default();
        }
    }

    public static void Save(int[] order)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, string.Join(",", order));
        }
        catch { }
    }
}
