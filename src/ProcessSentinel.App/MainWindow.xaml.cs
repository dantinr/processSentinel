using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using ProcessSentinel.Core;
using Activity = ProcessSentinel.Core.Activity;
using ActivityKind = ProcessSentinel.Core.ActivityKind;

namespace ProcessSentinel.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Activity> activities = new();
    private readonly ICollectionView activityView;
    private ICollectionView? processView;
    private readonly DispatcherTimer timer;
    private MonitorClient? client;
    private bool starting, closing, allowClose, preview, diagnostics;
    private readonly Stopwatch idleDuration = new();
    private Stopwatch duration => selectedSession?.Duration ?? idleDuration;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"行为哨兵 · ProcessSentinel {Protocol.Version}";
        activityView = CollectionViewSource.GetDefaultView(activities);
        activityView.Filter = MatchActivity;
        EventGrid.ItemsSource = activityView;
        monitoredProcessView = CollectionViewSource.GetDefaultView(monitoredProcesses);
        monitoredProcessView.Filter = MatchMonitoredProcess;
        MonitoredProcessGrid.ItemsSource = monitoredProcessView;
        SessionPicker.ItemsSource = sessions;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher);
        Loaded += async (_, _) => { if (!preview) await RefreshProcesses(); };
    }
    private async Task RefreshProcesses()
    {
        RefreshButton.IsEnabled = false;
        try
        {
            var selectedId = (ProcessList.SelectedItem as ProcessInfo)?.Id;
            var snapshot = await Task.Run(ProcessCatalog.Snapshot);
            processView = CollectionViewSource.GetDefaultView(snapshot.ToList());
            processView.Filter = value => value is ProcessInfo info && (string.IsNullOrWhiteSpace(ProcessSearch.Text)
                || info.Name.Contains(ProcessSearch.Text, StringComparison.OrdinalIgnoreCase)
                || info.Id.ToString().Contains(ProcessSearch.Text, StringComparison.OrdinalIgnoreCase)
                || info.Path.Contains(ProcessSearch.Text, StringComparison.OrdinalIgnoreCase));
            ProcessList.ItemsSource = processView;
            ProcessList.SelectedItem = snapshot.FirstOrDefault(x => x.Id == selectedId);
            ProcessCount.Text = $"{snapshot.Count} 个进程 · 支持名称 / PID / 路径搜索";
        }
        catch (Exception ex) { StatusText.Text = "读取进程失败：" + ex.Message; }
        finally { RefreshButton.IsEnabled = !starting && !stopping && !closing; }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshProcesses();
    private void ProcessSearch_Changed(object sender, TextChangedEventArgs e) => processView?.Refresh();
    private void ProcessList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (starting || stopping || closing) return;
        var selected = ProcessList.SelectedItem as ProcessInfo;
        SetControls(client?.Running == true);
        if (selected is null || selectedSession is not null) return;
        TargetTitle.Text = selected.Label;
        TargetPath.Text = selected.PathText;
    }
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessList.SelectedItem is ProcessInfo process) await BeginAsync(process, null);
    }
    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        var pick = new OpenFileDialog { Title = "选择要监控的程序", Filter = "Windows 程序 (*.exe)|*.exe", CheckFileExists = true };
        if (pick.ShowDialog(this) != true) return;
        var options = new LaunchDialog(pick.FileName) { Owner = this };
        if (options.ShowDialog() != true) return;
        SuspendedProgram? program = null;
        try
        {
            program = SuspendedProgram.Create(pick.FileName, options.Arguments);
            await BeginAsync(ProcessCatalog.Get(program.Id), program);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法启动程序", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { program?.Dispose(); }
    }
    private async Task BeginAsync(ProcessInfo process, SuspendedProgram? program)
    {
        if (starting || stopping || closing) return;
        if (FindRunningSession(process) is { } existing)
        {
            SessionPicker.SelectedItem = existing;
            StatusText.Text = "该进程已在监控中，已切换到对应会话。";
            return;
        }
        if (sessions.Count(x => x.Client.Running) >= MaximumConcurrentSessions)
        {
            StatusText.Text = $"最多同时监控 {MaximumConcurrentSessions} 个根程序，请先停止一个会话。";
            return;
        }
        starting = true;
        startupFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new MonitorSession(process, IncludeChildren.IsChecked == true);
        startupSession = session;
        session.UpdateLabel();
        sessions.Add(session);
        SessionPicker.SelectedItem = session;
        SetControls(true);
        try
        {
            TargetTitle.Text = process.Label;
            TargetPath.Text = process.PathText;
            EmptyTitle.Text = "等待采集器就绪";
            StatusText.Text = "请在 Windows UAC 提示中允许采集器运行…";
            RunBadge.Text = "●  正在连接采集器";
            await session.Client.StartAsync(new(process, session.IncludeChildren));
            if (closing || stopping) throw new OperationCanceledException();
            program?.Resume();
            session.Duration.Restart();
            RunBadge.Text = "●  正在监控";
            StatusText.Text = "监控已开始 · 完整记录正在保存";
            ExportButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            await session.Client.StopAsync();
            string message = ex is Win32Exception { NativeErrorCode: 1223 } ? "已取消管理员授权。" : ex is OperationCanceledException ? "连接已取消或超时。" : ex.Message;
            StatusText.Text = message;
            RunBadge.Text = "●  未开始监控";
            if (!closing && !diagnostics) MessageBox.Show(this, message, "未开始监控", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            session.Starting = false;
            session.UpdateLabel();
            startupSession = null;
            starting = false;
            startupFinished.TrySetResult();
            SetControls(client?.Running == true);
            Tick();
        }
    }
    private void SetControls(bool busy)
    {
        bool available = !starting && !stopping && !closing;
        bool capacity = sessions.Count(x => x.Client.Running) < MaximumConcurrentSessions;
        StartButton.IsEnabled = available && ProcessList.SelectedItem is ProcessInfo { Id: > 0, StartTimeUtcTicks: > 0 } info
            && info.Id != Environment.ProcessId && (capacity || FindRunningSession(info) is not null);
        LaunchButton.IsEnabled = available && capacity;
        StopButton.IsEnabled = !stopping && !closing && (starting || busy);
        RefreshButton.IsEnabled = available;
        ProcessList.IsEnabled = available;
        IncludeChildren.IsEnabled = available;
        SessionPicker.IsEnabled = available;
        UpdateSessionSummary();
    }
    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        if (starting) { startupSession?.Client.CancelStartup(); return; }
        await StopAsync();
    }
    private async Task StopAsync()
    {
        if (client is null || stopping) return;
        stopping = true;
        SetControls(false);
        try { await client.StopAsync(); Tick(); }
        finally { stopping = false; SetControls(client?.Running == true); }
    }
    private void Tick()
    {
        if (preview) return;
        foreach (var session in sessions) DrainSession(session);
        UpdateSessionSummary();
        if (client is null || selectedSession?.Starting == true) return;
        var snapshot = client.Processes;
        if (!ReferenceEquals(displayedProcessSnapshot, snapshot)) ApplyProcessSnapshot(snapshot);
        UpdateProcessSummary();
        TotalText.Text = client.Total.ToString("N0");
        TrafficText.Text = FormatBytes(client.Sent + client.Received);
        TrafficDetail.Text = $"↑ {FormatBytes(client.Sent)}   ↓ {FormatBytes(client.Received)}";
        FileText.Text = $"{client.Count(ActivityKind.File):N0} / {client.Count(ActivityKind.Registry):N0}";
        AlertText.Text = client.Alerts.ToString("N0");
        ProcessStat.Text = $"{client.ActiveProcesses} 个存活目标进程";
        EmptyState.Visibility = activityView.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = client.Total > 0 ? "没有符合筛选条件的记录" : "等待第一条行为记录";
        HistoryHint.Text = $"界面最近 {activities.Count:N0} 条 · 原始日志 {client.Total:N0} 条 · ETW 丢失 {client.EtwLost:N0} · 采集队列丢失 {client.QueueLost:N0}";
        if (client.DisplaySkipped > 0) HistoryHint.Text += $" · 界面省略 {client.DisplaySkipped:N0} 条";
        if (client.Running) StatusText.Text = $"正在监控 {duration.Elapsed.ToString(@"hh\:mm\:ss")} · {client.ActiveProcesses} 个存活进程 · 日志持续保存";
        else
        {
            duration.Stop();
            StatusText.Text = string.IsNullOrEmpty(client.Error) ? $"当前监控已停止 · 日志保存于 {client.JournalPath}" : "当前采集异常 · " + client.Error;
        }
        if (client.EtwLost + client.QueueLost > 0) StatusText.Text += " · 有事件丢失，日志不完整";
        ExportButton.IsEnabled = !string.IsNullOrEmpty(client.JournalPath);
        SetControls(client.Running);
    }

    private void AppendActivityBatch(IEnumerable<Activity> batch)
    {
        // DeferRefresh batches view settings, not ObservableCollection mutations.
        // ListCollectionView reads currency while handling each Add/Remove notification.
        foreach (var value in batch)
        {
            activities.Add(value);
            if (activities.Count > 5000) activities.RemoveAt(0);
        }
    }
    private void ResetCounters()
    {
        TotalText.Text = AlertText.Text = "0"; FileText.Text = "0 / 0"; TrafficText.Text = "0 B"; TrafficDetail.Text = "↑ 0 B   ↓ 0 B"; ProcessStat.Text = "等待采集器统计";
        DetailTitle.Text = "事件证据"; DetailText.Text = "选择一条记录，查看完整目标和触发原因。"; ExportButton.IsEnabled = false; EmptyState.Visibility = Visibility.Visible;
    }
    private bool MatchActivity(object item)
    {
        if (item is not Activity value) return false;
        int index = KindFilter?.SelectedIndex ?? 0;
        if (index > 0 && (int)value.Kind != index - 1) return false;
        if (RiskOnly?.IsChecked == true && value.Risk == RiskLevel.None) return false;
        var text = EventSearch?.Text ?? "";
        return string.IsNullOrWhiteSpace(text) || $"{value.ProcessText} {value.Target} {value.Operation} {value.Reason} {value.Detail}".Contains(text, StringComparison.OrdinalIgnoreCase);
    }
    private void Filter_Changed(object sender, RoutedEventArgs e) { activityView?.Refresh(); if (activityView is not null) EmptyState.Visibility = activityView.IsEmpty ? Visibility.Visible : Visibility.Collapsed; }
    private void EventSearch_Changed(object sender, TextChangedEventArgs e) => Filter_Changed(sender, e);
    private void EventGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EventGrid.SelectedItem is not Activity value) return;
        DetailTitle.Text = $"{value.KindText} / {value.Operation} · {value.RiskText}{(string.IsNullOrEmpty(value.RuleId) ? "" : " · " + value.RuleId)}";
        DetailText.Text = $"{value.Time.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}   #{value.Sequence}   {value.ProcessText}\n目标：{value.Target}\n{value.Evidence}";
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (client is null) return;
        var dialog = new SaveFileDialog { Title = "导出全部记录（含被界面省略的记录）", Filter = "原始日志 (*.jsonl)|*.jsonl|CSV 表格 (*.csv)|*.csv", FileName = "行为记录-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        ExportButton.IsEnabled = false;
        try
        {
            var current = client;
            string temporary = Path.Combine(Path.GetTempPath(), "ProcessSentinel-export-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                current.CopyJournal(temporary);
                await Task.Run(() =>
                {
                    if (dialog.FilterIndex == 2) EvidenceJournal.ExportCsv(temporary, dialog.FileName);
                    else File.Copy(temporary, dialog.FileName, true);
                });
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            StatusText.Text = "已导出到 " + dialog.FileName;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { ExportButton.IsEnabled = true; }
    }
    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProcessSentinel", "Sessions");
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + directory + "\"") { UseShellExecute = true });
    }
    private void Help_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "1. 选择进程后点击“添加监控”，或选择 .exe 启动并监控；最多同时监控 4 个根程序。\n2. 每个程序独立请求采集器权限、保存日志；新启动的目标使用界面当前权限。\n3. 顶部“查看程序”切换行为、统计和进程名单，其他程序持续监控。\n4. “停止当前”仅停止选中会话，“全部停止”停止所有会话，目标程序继续运行。\n5. 左侧可继续选择并添加程序；子进程选项只用于新监控。已监控的根 PID 不会重复添加。\n6. 已停止会话可查看、导出或移除记录；移除不会删除日志。\n\n覆盖：文件操作、TCP/UDP 端点与传输字节、注册表、子进程、模块加载。\n限制：只记录监控开始后的事件；无法保证观察所有行为，不读取 HTTPS 内容、文件内容或注册表值，不检测内存注入。文件操作默认是请求，不能视为已成功。\n\n“需复核 / 高关注”表示行为线索，不能直接认定恶意；没有提示也不能证明安全。ETW 或队列丢失会明确显示。\n\nUAC 需要使用同一个 Windows 用户；换用其他管理员账户无法连接采集器。多个程序的子进程范围重叠时，同一事件可能分别记录到各自日志。",
        "行为哨兵 · 第一版使用说明", MessageBoxButton.OK, MessageBoxImage.Information);
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose || preview) return;
        timer.Stop();
        if (!starting && sessions.Count == 0 && client is null) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        SetControls(false);
        _ = CompleteCloseAsync();
    }

    private async Task CompleteCloseAsync()
    {
        // A completed Stop/Dispose task may not yield. Always let the original Closing
        // event return before requesting Close again, or WPF rejects the nested close.
        await Dispatcher.Yield(DispatcherPriority.Background);
        try
        {
            if (starting)
            {
                startupSession?.Client.CancelStartup();
                if (startupFinished is { } completion) await completion.Task;
            }
            // A stop/removal handler may still own a client. Let it finish before disposal.
            while (stopping) await Task.Delay(50);
            await Task.WhenAll(sessions.Select(async x => await x.Client.DisposeAsync()));
            sessions.Clear();
            selectedSession = null;
            client = null;
        }
        catch (Exception ex)
        {
            StatusText.Text = "关闭采集器失败：" + ex.Message;
            if (!diagnostics) MessageBox.Show(this, StatusText.Text, "停止采集", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            allowClose = true;
            Close();
        }
    }
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return $"{value:0.#} {units[i]}";
    }
    public void SetPreviewData()
    {
        preview = true;
        ProcessInfo[] list = [new(8420, 2400, "sample-app.exe", @"D:\Apps\Sample\sample-app.exe", 1), new(13512, 8420, "powershell.exe", "", 1), new(6100, 1, "explorer.exe", "", 1), new(9044, 1, "msedge.exe", "", 1), new(9048, 9044, "msedge.exe", "", 1), new(4500, 1, "notepad.exe", "", 1)];
        long previewStart = DateTime.UtcNow.AddMinutes(-15).Ticks;
        list[0] = list[0] with { StartTimeUtcTicks = previewStart };
        list[1] = list[1] with { StartTimeUtcTicks = previewStart + TimeSpan.TicksPerSecond };
        ProcessList.ItemsSource = list;
        ProcessList.SelectedIndex = 0;
        ProcessCount.Text = "示例进程 · 界面预览";
        var previewSession = new MonitorSession(list[0], true) { Starting = false };
        var otherPreviewSession = new MonitorSession(list[3], true) { Starting = false };
        sessions.Add(previewSession);
        sessions.Add(otherPreviewSession);
        SessionPicker.SelectedItem = previewSession;
        previewSession.UpdateLabel("示例：监控中");
        otherPreviewSession.UpdateLabel("示例：监控中");
        SessionCount.Text = "示例 · 同时监控 2 个程序";
        TargetTitle.Text = "sample-app.exe · PID 8420";
        TargetPath.Text = @"D:\Apps\Sample\sample-app.exe";
        RunBadge.Text = "●  示例数据预览";
        TotalText.Text = "1,284"; TrafficText.Text = "2.4 MB"; TrafficDetail.Text = "↑ 420 KB   ↓ 2 MB"; FileText.Text = "926 / 184"; AlertText.Text = "3"; ProcessStat.Text = "2 个存活目标进程";
        ApplyProcessSnapshot([new(list[0], true, true), new(list[1] with { Path = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" }, false, true)]);
        var samples = new[] {
            new Activity { Kind = ActivityKind.File, Operation = "读取", Target = @"C:\Users\demo\AppData\Local\Google\Chrome\User Data\Default\Login Data", Detail = "请求读取 4,096 字节；未确认完成状态。" },
            new Activity { Kind = ActivityKind.Network, Operation = "连接", Target = "192.168.1.8:52140 → 203.0.113.20:443", Detail = "TCP/IPv4 · 只记录端点，不读取通信内容。" },
            new Activity { Kind = ActivityKind.File, Operation = "写入", Target = @"D:\Apps\Sample\logs\app.log", Detail = "请求写入 512 字节；未确认完成状态。" },
            new Activity { Kind = ActivityKind.Registry, Operation = "设置值", Target = @"\REGISTRY\USER\S-1-5-21-demo\Software\Microsoft\Windows\CurrentVersion\Run\Sample", Detail = "NTSTATUS：0x00000000" },
            new Activity { Kind = ActivityKind.Process, Operation = "启动", Target = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", Detail = "父进程 PID 8420\n命令行：powershell.exe -NoProfile -EncodedCommand [示例]" },
            new Activity { Kind = ActivityKind.Image, Operation = "加载", Target = @"C:\Windows\System32\winhttp.dll", Detail = "模块大小：169,984 字节。" },
            new Activity { Kind = ActivityKind.Network, Operation = "发送", Target = "192.168.1.8:52140 → 203.0.113.20:443", Detail = "TCP/IPv4 · 1,460 字节" },
            new Activity { Kind = ActivityKind.File, Operation = "打开/创建", Target = @"D:\Apps\Sample\config.json", Detail = "ETW 请求事件，未确认完成状态。" }
        };
        for (int i = 0; i < samples.Length; i++) activities.Add(RiskEngine.Evaluate(samples[i] with { Sequence = i + 1, Time = DateTimeOffset.Now.AddMilliseconds(i * 137), ProcessId = 8420, ProcessName = "sample-app.exe" }));
        EmptyState.Visibility = Visibility.Collapsed;
        EventGrid.SelectedIndex = 0;
        StopButton.IsEnabled = true;
        StopAllButton.IsEnabled = true;
        ExportButton.IsEnabled = true;
        StatusText.Text = "界面预览 · 当前显示的是示例数据，不代表任何真实程序的行为";
    }
}
