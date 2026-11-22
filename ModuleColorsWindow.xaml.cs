// ==============================================================================
// ModuleColorsWindow.xaml.cs — порядок цветов модулей ВОК: 12 строк
// «Модуль N ▾ цвет». Порядок применяется/сохраняется через окно настроек
// (эта кнопка «Зберегти» только возвращает выбор в SettingsWindow).
// ==============================================================================
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ExfoTrcAnalyzerGUI;

public partial class ModuleColorsWindow : Window
{
    private ComboBox[] _colorCombos = [];

    // Результат — порядок цветов (позиция = номер модуля, значение = номер цвета 1..12)
    public int[] ResultOrder { get; private set; }

    public ModuleColorsWindow(int[] order)
    {
        InitializeComponent();
        ResultOrder = (int[])(order.Length == 12 ? order : ModuleColors.Default()).Clone();
        BuildPanel(ResultOrder);
    }

    private void BuildPanel(int[] order)
    {
        ColorOrderPanel.Children.Clear();
        _colorCombos = new ComboBox[12];
        var swatchStroke = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));

        for (int m = 0; m < 12; m++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 12, 2) };
            row.Children.Add(new TextBlock
            {
                Text = Loc.F("module_fmt", m + 1),
                MinWidth = 64,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var cb = new ComboBox { Width = 170, VerticalAlignment = VerticalAlignment.Center };
            for (int c = 1; c <= 12; c++)
            {
                var swatch = new System.Windows.Shapes.Rectangle
                {
                    Width = 12, Height = 12, RadiusX = 3, RadiusY = 3,
                    Fill = new SolidColorBrush(ModuleColorConverter.StandardColor(c)),
                    Stroke = swatchStroke, StrokeThickness = 1,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var item = new StackPanel { Orientation = Orientation.Horizontal };
                item.Children.Add(swatch);
                item.Children.Add(new TextBlock
                {
                    Text = Loc.T($"color_{c}"),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                cb.Items.Add(item); // элементы — готовые панели: свотч + название цвета
            }
            cb.SelectedIndex = Math.Clamp(order[m], 1, 12) - 1;
            _colorCombos[m] = cb;
            row.Children.Add(cb);
            ColorOrderPanel.Children.Add(row);
        }
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        ResultOrder = Enumerable.Range(0, 12)
            .Select(i => Math.Clamp(_colorCombos[i].SelectedIndex + 1, 1, 12))
            .ToArray();
        DialogResult = true;
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
        => BuildPanel(ModuleColors.Default()); // стандартный порядок TIA-598
}
