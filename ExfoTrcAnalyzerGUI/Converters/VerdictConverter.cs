// ==============================================================================
// VerdictConverter.cs — конвертеры для отображения вердикта и подсказок.
//   VerdictBrushConverter  — точка в списке файлов: PASS зелёная, FAIL красная
//   VerdictTextConverter   — ячейка «Вердикт»: PASS / FAIL / пусто
//   NullIfEmptyConverter   — ToolTip строки: null если текст пуст (нет пустых
//                            всплывающих подсказок)
//   FailsWarningsTooltipConverter — ToolTip вкладки «Волокна»: причины +
//                            предупреждения в одном тексте
// ==============================================================================
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ExfoTrcAnalyzerGUI;

public class DotBrushConverter : IValueConverter
{
    private static readonly Brush Green = Brushes.ForestGreen;
    private static readonly Brush Red = Brushes.Firebrick;
    private static readonly Brush Yellow = new SolidColorBrush(Color.FromRgb(0xD4, 0xA0, 0x17));
    private static readonly Brush Gray = Brushes.Gray;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            DotStatus.Green => Green,
            DotStatus.Yellow => Yellow,
            DotStatus.Red => Red,
            _ => Gray,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class VerdictTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            Verdict.Pass => "PASS",
            Verdict.Atn => "ATTN",
            Verdict.Fail => "FAIL",
            _ => "",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class NullIfEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? null : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// true -> Collapsed, false -> Visible (скрыть кружок у строки «Все»)
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// Сортировка колонки -> глиф стрелки в заголовке: null -> "", Ascending -> "▲", Descending -> "▼"
public class SortArrowGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is System.ComponentModel.ListSortDirection d
            ? (d == System.ComponentModel.ListSortDirection.Ascending ? "▲" : "▼")
            : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class FailsWarningsTooltipConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        string fails = values.Length > 0 ? values[0] as string ?? "" : "";
        string warnings = values.Length > 1 ? values[1] as string ?? "" : "";
        if (string.IsNullOrWhiteSpace(fails) && string.IsNullOrWhiteSpace(warnings))
            return null;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(fails))
            parts.Add(Loc.T("tip_fails_prefix") + "\n" + fails.Replace(" | ", "\n"));
        if (!string.IsNullOrWhiteSpace(warnings))
            parts.Add(Loc.T("tip_warnings_prefix") + "\n" + warnings.Replace(" | ", "\n"));
        return string.Join("\n\n", parts);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
