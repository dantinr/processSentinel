using System.Windows;

namespace ProcessSentinel.App;

public partial class MainWindow
{
    private AppSettings settings = new();

    private void ApplySettings(AppSettings value)
    {
        IncludeChildren.IsChecked = value.IncludeProgramTree;
        RiskOnly.IsChecked = value.RiskOnly;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (starting || stopping || closing) return;
        var dialog = new SettingsWindow(settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        settings = dialog.Settings;
        ApplySettings(settings);
        StatusText.Text = "设置已保存 · 监控范围用于新会话，风险筛选已更新";
    }

    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();
}
