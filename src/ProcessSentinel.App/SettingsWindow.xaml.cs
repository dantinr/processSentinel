using System.IO;
using System.Windows;

namespace ProcessSentinel.App;

public partial class SettingsWindow : Window
{
    internal AppSettings Settings { get; private set; }

    internal SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        ProgramTreeOption.IsChecked = settings.IncludeProgramTree;
        RiskOnlyOption.IsChecked = settings.RiskOnly;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var next = new AppSettings
        {
            IncludeProgramTree = ProgramTreeOption.IsChecked == true,
            RiskOnly = RiskOnlyOption.IsChecked == true
        };
        try
        {
            next.Save();
            Settings = next;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "设置未保存：" + ex.Message, "无法保存设置", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
