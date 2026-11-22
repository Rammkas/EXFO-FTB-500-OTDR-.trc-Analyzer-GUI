// ==============================================================================
// ModuleColorConverter.cs — стандартные цвета модулей/волокон ВОК (TIA-598):
// 1 синий, 2 оранжевый, 3 зелёный, 4 коричневый, 5 серо-голубой (slate),
// 6 белый, 7 красный, 8 чёрный, 9 жёлтый, 10 фиолетовый, 11 индиго, 12 розовый.
// >12 — последовательность повторяется. Белый/чёрный обводятся рамкой
// (видимость и на светлой, и на тёмной теме).
// ==============================================================================
using System.Globalization;
using System.Windows.Media;
using System.Windows.Data;

namespace ExfoTrcAnalyzerGUI;

public class ModuleColorConverter : IValueConverter
{
    // TIA-598-C / IEC 60304 порядок (1..12)
    private static readonly Color[] Standard =
    [
        Color.FromRgb(0x25, 0x63, 0xEB), // 1 синий
        Color.FromRgb(0xF9, 0x73, 0x16), // 2 оранжевый
        Color.FromRgb(0x16, 0xA3, 0x4A), // 3 зелёный
        Color.FromRgb(0x8B, 0x5A, 0x2B), // 4 коричневый
        Color.FromRgb(0x6B, 0x72, 0x80), // 5 slate (серо-голубой)
        Color.FromRgb(0xFF, 0xFF, 0xFF), // 6 белый
        Color.FromRgb(0xDC, 0x26, 0x26), // 7 красный
        Color.FromRgb(0x11, 0x18, 0x27), // 8 чёрный
        Color.FromRgb(0xEA, 0xB3, 0x08), // 9 жёлтый
        Color.FromRgb(0x7C, 0x3A, 0xED), // 10 фиолетовый
        Color.FromRgb(0x43, 0x38, 0xCA), // 11 индиго
        Color.FromRgb(0xDB, 0x27, 0x77), // 12 розовый
    ];

    public static Color StandardColor(int colorIdx)
        => Standard[Math.Clamp(colorIdx, 1, Standard.Length) - 1];

    // цвет модуля с учётом пользовательского порядка (AppSettings.ModuleColorOrder)
    public static Brush BrushFor(int? moduleNo)
    {
        if (moduleNo == null || moduleNo < 1)
            return Brushes.Transparent;
        return new SolidColorBrush(StandardColor(AppSettings.Current.ColorForModule(moduleNo.Value)));
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BrushFor(value as int?);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
