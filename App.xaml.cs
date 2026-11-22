using System.Windows;

namespace ExfoTrcAnalyzerGUI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // словарь строк текущего языка — в ресурсы приложения (XAML DynamicResource)
        Loc.ApplyToApplication();
        base.OnStartup(e);
    }
}
