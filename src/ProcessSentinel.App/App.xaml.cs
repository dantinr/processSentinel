using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using ProcessSentinel.Core;
using ActivityKind = ProcessSentinel.Core.ActivityKind;

namespace ProcessSentinel.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 3 && e.Args[0] == "--verify-live-ui" && int.TryParse(e.Args[2], out int targetPid))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = VerifyLiveUiAsync(e.Args[1], targetPid);
            return;
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--verify-ui")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = VerifyUiAsync(e.Args[1], e.Args.Skip(2).ToArray());
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--verify-collector")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = VerifyCollectorAsync(e.Args[1], e.Args[2]);
            return;
        }
        var window = new MainWindow();
        if (e.Args.Length == 2 && e.Args[0] == "--render-preview")
        {
            window.SetPreviewData();
            window.Width = 1440;
            window.Height = 960;
            window.Show();
            window.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    window.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    var png = new PngBitmapEncoder();
                    png.Frames.Add(BitmapFrame.Create(bitmap));
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!);
                    using var stream = File.Create(e.Args[1]);
                    png.Save(stream);
                }
                finally { Shutdown(); }
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        else window.Show();
    }

    private async Task VerifyCollectorAsync(string reportPath, string fixturePath)
    {
        var lines = new List<string>();
        string directory = Path.Combine(Path.GetTempPath(), "ProcessSentinel-probe-" + Guid.NewGuid().ToString("N"));
        int code = 1;
        try
        {
            await using var client = new MonitorClient();
            using var suspended = SuspendedProgram.Create(fixturePath, "--fixture \"" + directory + "\"");
            using var target = Process.GetProcessById(suspended.Id);
            await client.StartAsync(new(ProcessCatalog.Get(suspended.Id), true));
            lines.Add("PASS: ordinary UI client connected to elevated collector over user-only pipe");
            suspended.Resume();
            await target.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            await Task.Delay(1500);
            await client.StopAsync();
            if (!string.IsNullOrEmpty(client.Error)) throw new IOException(client.Error);
            var events = EvidenceJournal.ReadEvents(client.JournalPath).ToArray();
            void Verify(bool test, string text) { if (!test) throw new InvalidOperationException(text); lines.Add("PASS: " + text); }
            Verify(events.Any(x => x.Kind == ActivityKind.File && x.Operation == "写入" && x.Target.Contains("ProcessSentinel-probe-", StringComparison.OrdinalIgnoreCase)), "file event delivered and persisted through real collector");
            Verify(events.Any(x => x.Kind == ActivityKind.Network && x.Operation == "发送" && x.Bytes > 0), "network bytes delivered and persisted through real collector");
            Verify(events.Any(x => x.Kind == ActivityKind.Registry && x.Operation == "设置值" && x.Target.Contains("ProcessSentinelSelfTest", StringComparison.OrdinalIgnoreCase)), "registry path delivered and persisted through real collector");
            Verify(events.Any(x => x.Kind == ActivityKind.Process && x.Operation == "启动" && x.ProcessId != target.Id), "descendant tracked through real collector");
            Verify(!client.Running && events.LongLength == client.Total, "graceful stop drains all pipe events to journal");
            Verify(client.QueueLost == 0 && client.EtwLost == 0, "probe reports zero ETW or collector-queue loss");
            Verify(!EtwMonitor.IsAdministrator(), "UI test ran with ordinary permissions");
            lines.Add($"Captured {client.Total} events. Evidence: {client.JournalPath}");
            code = 0;
        }
        catch (Exception ex) { lines.Add("FAIL: " + ex); }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            await File.WriteAllLinesAsync(reportPath, lines);
            Shutdown(code);
        }
    }

    private async Task VerifyUiAsync(string reportPath, string[] journalPaths)
    {
        var lines = new List<string>();
        var window = new MainWindow();
        window.PrepareForDiagnostics();
        int code = 1;
        try
        {
            window.Show();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var events = journalPaths.Length > 0
                ? journalPaths.SelectMany(EvidenceJournal.ReadEvents).ToArray()
                : new[] { new ProcessSentinel.Core.Activity { Sequence = 1, Kind = ActivityKind.Network, Operation = "接收", Target = "127.0.0.1:1234 → 127.0.0.1:5678" } };
            await window.VerifyUiAsync(events, lines.Add);
            code = 0;
        }
        catch (Exception ex) { lines.Add("FAIL: " + ex); }
        finally
        {
            try
            {
                await window.CloseForDiagnosticsAsync();
                lines.Add("PASS: idle window closes normally without reentering Closing");
            }
            catch (Exception ex) { lines.Add("FAIL: window close: " + ex); code = 1; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            await File.WriteAllLinesAsync(reportPath, lines);
            Shutdown(code);
        }
    }

    private async Task VerifyLiveUiAsync(string reportPath, int targetPid)
    {
        var lines = new List<string>();
        var window = new MainWindow();
        window.PrepareForDiagnostics();
        int code = 1;
        try
        {
            var root = ProcessCatalog.Get(targetPid);
            window.Show();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await window.VerifyLiveUiAsync(root, lines.Add);
            code = 0;
        }
        catch (Exception ex) { lines.Add("FAIL: " + ex); }
        finally
        {
            try
            {
                await window.CloseForDiagnosticsAsync();
                lines.Add("PASS: stopped live monitor and window close complete normally");
            }
            catch (Exception ex) { lines.Add("FAIL: window close: " + ex); code = 1; }
            await File.WriteAllLinesAsync(reportPath, lines);
            Shutdown(code);
        }
    }
}
