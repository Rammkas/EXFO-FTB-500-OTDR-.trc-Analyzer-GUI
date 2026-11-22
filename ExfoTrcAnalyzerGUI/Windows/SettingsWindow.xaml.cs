// ==============================================================================
// SettingsWindow.xaml.cs — окно порогов анализа (аналог otdr_config.txt).
// Числа принимаются и с точкой, и с запятой (как в консольной версии).
// ==============================================================================
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ExfoTrcAnalyzerGUI.Windows;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        LoadFrom(AppSettings.Current);
    }

    private void LoadFrom(AppSettings s)
    {
        HasBufferSpool.IsChecked = s.HasBufferSpool;
        BufferMinM.Text = s.BufferMinM.ToString(CultureInfo.InvariantCulture);
        BufferMaxM.Text = s.BufferMaxM.ToString(CultureInfo.InvariantCulture);
        Atten1310Max.Text = s.Atten1310Max.ToString(CultureInfo.InvariantCulture);
        Atten1550Max.Text = s.Atten1550Max.ToString(CultureInfo.InvariantCulture);
        SpliceLossMax.Text = s.SpliceLossMax.ToString(CultureInfo.InvariantCulture);
        MatchToleranceM.Text = s.MatchToleranceM.ToString(CultureInfo.InvariantCulture);
        QcConnectorLossMax.Text = s.QcConnectorLossMax.ToString(CultureInfo.InvariantCulture);
        BorderlinePercent.Text = s.BorderlinePercent.ToString(CultureInfo.InvariantCulture);
        CheckQcConnector.IsChecked = s.CheckQcConnector;
        ShowLosses.IsChecked = s.ShowLosses;
        CompareMode.IsChecked = s.CompareMode;
        LangCombo.SelectedIndex = Array.IndexOf(Loc.Languages, Loc.Language);
        _colorOrder = (int[])(s.ModuleColorOrder.Length == 12 ? s.ModuleColorOrder : ModuleColors.Default()).Clone();
    }

    // Порядок цветов модулей — рабочий копия; редактируется в ModuleColorsWindow,
    // сохраняется только вместе с окном настроек («Зберегти»)
    private int[] _colorOrder = ModuleColors.Default();

    private void BtnModuleColors_Click(object sender, RoutedEventArgs e)
    {
        var win = new ModuleColorsWindow(_colorOrder) { Owner = this };
        if (win.ShowDialog() == true)
            _colorOrder = win.ResultOrder;
    }

    private static bool TryNum(TextBox box, out double value)
        => double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float,
                           CultureInfo.InvariantCulture, out value);

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var errors = new System.Collections.Generic.List<string>();

        double bufMin = 0, bufMax = 0, a13 = 0, a15 = 0, splice = 0, match = 0, qc = 0, blPct = 0;
        void Check(string nameKey, TextBox box, out double v)
        {
            if (!TryNum(box, out v))
                errors.Add(Loc.F("val_not_number", Loc.T(nameKey), box.Text));
            else if (v <= 0)
                errors.Add(Loc.F("val_positive", Loc.T(nameKey)));
        }
        Check("val_buf_min", BufferMinM, out bufMin);
        Check("val_buf_max", BufferMaxM, out bufMax);
        Check("val_att13", Atten1310Max, out a13);
        Check("val_att15", Atten1550Max, out a15);
        Check("val_splice", SpliceLossMax, out splice);
        Check("val_match", MatchToleranceM, out match);
        Check("val_qc", QcConnectorLossMax, out qc);
        Check("val_yellow", BorderlinePercent, out blPct);
        if (blPct > 0 && blPct >= 100)
            errors.Add(Loc.T("val_yellow_100"));

        if (errors.Count == 0 && bufMin >= bufMax)
            errors.Add(Loc.T("val_buffer_order"));

        if (errors.Count > 0)
        {
            ErrorText.Text = string.Join("; ", errors);
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        var s = new AppSettings
        {
            HasBufferSpool = HasBufferSpool.IsChecked == true,
            BufferMinM = bufMin,
            BufferMaxM = bufMax,
            Atten1310Max = a13,
            Atten1550Max = a15,
            SpliceLossMax = splice,
            MatchToleranceM = match,
            QcConnectorLossMax = qc,
            BorderlinePercent = blPct,
            CheckQcConnector = CheckQcConnector.IsChecked == true,
            ShowLosses = ShowLosses.IsChecked == true,
            CompareMode = CompareMode.IsChecked == true,
        };

        // порядок цветов модулей (1..12; из диалога «Кольори модулів…»)
        s.ModuleColorOrder = _colorOrder;
        ModuleColors.Save(s.ModuleColorOrder);

        AppSettings.Current = s; // заменяем текущий экземпляр настроек

        // язык — из выпадающего списка (смена применилась бы и без сохранения
        // порогов, поэтому делаем это здесь же; MainWindow после Ok пересчитает файлы)
        int langIdx = LangCombo.SelectedIndex >= 0 ? LangCombo.SelectedIndex : 0;
        if (Loc.Language != Loc.Languages[langIdx])
            Loc.SetLanguage(Loc.Languages[langIdx]);

        DialogResult = true;
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        LoadFrom(new AppSettings()); // встроенные значения по умолчанию
        LangCombo.SelectedIndex = Array.IndexOf(Loc.Languages, Loc.Uk); // язык по умолчанию — украинский
    }
}
