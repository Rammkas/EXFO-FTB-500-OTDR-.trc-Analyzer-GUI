using System.Windows;

namespace ExfoTrcAnalyzerGUI;

// Диалог «что включить в отчёт»: карта + три таблицы
public partial class PrintOptionsWindow : Window
{
    public PrintOptionsWindow() => InitializeComponent();

    public bool WantMap => ChkMap.IsChecked == true;
    public bool WantFibers => ChkFibers.IsChecked == true;
    public bool WantSections => ChkSections.IsChecked == true;
    public bool WantEvents => ChkEvents.IsChecked == true;

    private void BtnOk_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
