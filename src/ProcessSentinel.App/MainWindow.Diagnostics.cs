using System.ComponentModel;
using System.Windows.Threading;
using ProcessSentinel.Core;
using Activity = ProcessSentinel.Core.Activity;

namespace ProcessSentinel.App;

public partial class MainWindow
{
    internal void ShowProcessPreview()
    {
        MonitorTabs.SelectedIndex = 1;
        MonitoredProcessGrid.SelectedIndex = 0;
    }

    internal void PrepareForDiagnostics()
    {
        preview = true;
        diagnostics = true;
        timer.Stop();
        ShowActivated = false;
        ShowInTaskbar = false;
        Opacity = 0;
    }

    // Exercises the same mutation path as Tick, with a live bound DataGrid and dispatcher.
    internal async Task VerifyUiAsync(IReadOnlyList<Activity> saved, Action<string> passed)
    {
        void Check(bool result, string message)
        {
            if (!result) throw new InvalidOperationException(message);
            passed("PASS: " + message);
        }
        Check(saved.Count > 0, $"loaded {saved.Count} journal events for UI replay");
        AppendActivityBatch(saved);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(EventGrid.Items.Count == Math.Min(saved.Count, 5000), "journal replay updates the bound DataGrid");

        EventGrid.SelectedItem = saved.Last();
        activityView.MoveCurrentTo(saved.Last());
        AppendActivityBatch(saved);
        Check(EventGrid.SelectedItem is Activity, "incoming events preserve selection and current item");
        activityView.SortDescriptions.Add(new SortDescription(nameof(Activity.Sequence), ListSortDirection.Descending));
        AppendActivityBatch(saved);
        Check(activityView.Cast<Activity>().First().Sequence == saved.Max(x => x.Sequence), "sorted grid accepts live incoming events");

        KindFilter.SelectedIndex = 2;
        AppendActivityBatch(saved);
        Check(activityView.Cast<Activity>().All(x => x.Kind == ActivityKind.Network), "category filtering works during incoming events");
        KindFilter.SelectedIndex = 0;
        RiskOnly.IsChecked = true;
        var flagged = saved[0] with { Sequence = 10001, Risk = RiskLevel.Attention, Reason = "UI regression fixture" };
        AppendActivityBatch([flagged]);
        Check(activityView.Cast<Activity>().Any(x => ReferenceEquals(x, flagged)) && activityView.Cast<Activity>().All(x => x.Risk != RiskLevel.None), "risk filter accepts a new flagged event");
        RiskOnly.IsChecked = false;
        EventSearch.Text = "UI regression fixture";
        AppendActivityBatch([flagged]);
        Check(!activityView.IsEmpty && activityView.Cast<Activity>().All(x => x.Reason.Contains("UI regression fixture")), "keyword filter works while records arrive");
        EventSearch.Clear();
        activityView.SortDescriptions.Clear();

        for (int offset = 0; offset < 12000; offset += 600)
        {
            EventGrid.SelectedItem = activities.First();
            activityView.MoveCurrentTo(activities.First());
            AppendActivityBatch(Enumerable.Range(offset, 600).Select(i => saved[i % saved.Count] with { Sequence = 20000 + i }));
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        Check(activities.Count == 5000 && EventGrid.Items.Count == 5000 && activities.Last().Sequence == 31999,
            "12,000 incoming events retain the newest 5,000 and evict selected old rows safely");
        EventGrid.SelectedItem = activities.Last();
        Check(DetailText.Text.Contains(activities.Last().Target), "selected event still displays complete evidence after retention trimming");
        activities.Clear();
        AppendActivityBatch(saved);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(EventGrid.Items.Count == Math.Min(saved.Count, 5000), "clear and restart update the bound grid safely");

        MonitorTabs.SelectedIndex = 1;
        var rootProcess = new ProcessInfo(101, 1, "root.exe", @"C:\Apps\root.exe", DateTime.UtcNow.Ticks);
        var idleChild = new ProcessInfo(102, 101, "idle-child.exe", @"C:\Apps\idle-child.exe", rootProcess.StartTimeUtcTicks + 1);
        ApplyProcessSnapshot([new(rootProcess, true, true), new(idleChild, false, true)]);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(MonitoredProcessGrid.Items.Count == 2 && monitoredProcesses.All(x => x.IsRunning),
            "bound process grid includes a root and a child with no activity events");
        MonitoredProcessGrid.SelectedItem = monitoredProcesses.Single(x => x.Process.Id == idleChild.Id);
        monitoredProcessView.SortDescriptions.Add(new SortDescription("Process.Id", ListSortDirection.Descending));
        var newChild = new ProcessInfo(103, 102, "new-child.exe", @"C:\Apps\new-child.exe", rootProcess.StartTimeUtcTicks + 2);
        ApplyProcessSnapshot([new(rootProcess, true, true), new(idleChild, false, true), new(newChild, false, true)]);
        Check(MonitoredProcessGrid.Items.Count == 3 && MonitoredProcessGrid.SelectedItem is TrackedProcess { Process.Id: 102 }
            && DetailText.Text.Contains(idleChild.Path), "new descendants update the sorted roster while preserving selection and complete path");
        LiveProcessesOnly.IsChecked = true;
        ApplyProcessSnapshot([new(rootProcess, true, true), new(idleChild, false, false), new(newChild, false, true)]);
        Check(MonitoredProcessGrid.Items.Count == 2 && monitoredProcesses.Count == 3 && monitoredProcesses.Single(x => x.Process.Id == 102).StateText == "已退出",
            "process exit updates the live filter while retaining the exited row");
        LiveProcessesOnly.IsChecked = false;
        MonitoredProcessSearch.Text = newChild.Path;
        Check(MonitoredProcessGrid.Items.Count == 1 && ((TrackedProcess)MonitoredProcessGrid.Items[0]).Process.Id == 103,
            "process roster supports full-path search");
        MonitoredProcessSearch.Clear();
        MonitoredProcessGrid.SelectedItem = monitoredProcesses.Single(x => x.Process.Id == 102);
        var reusedChild = idleChild with { StartTimeUtcTicks = rootProcess.StartTimeUtcTicks + 3, Name = "reused-pid.exe" };
        ApplyProcessSnapshot([new(rootProcess, true, false), new(idleChild, false, false), new(newChild, false, true), new(reusedChild, false, true)]);
        Check(monitoredProcesses.Count(x => x.Process.Id == 102) == 2 && MonitoredProcessGrid.SelectedItem is TrackedProcess selected
            && selected.Process.StartTimeUtcTicks == idleChild.StartTimeUtcTicks && !selected.IsRunning,
            "PID reuse keeps separate rows and preserves the selected process identity after parent exit");
        ClearMonitoredProcesses();
        Check(MonitoredProcessGrid.Items.Count == 0 && MonitoredProcessTab.Header.ToString()!.Contains("0"),
            "restarting monitoring clears the previous process roster");
        MonitorTabs.SelectedIndex = 0;
    }

    internal async Task VerifyLiveUiAsync(ProcessInfo root, Action<string> passed)
    {
        preview = false;
        await BeginAsync(root, null);
        if (client?.Running != true) throw new InvalidOperationException("Unable to start live UI monitor: " + StatusText.Text);
        passed($"PASS: live UI monitoring started for {root.Name}, PID {root.Id}, version {Protocol.Version}");
        timer.Start();
        try
        {
            await Task.Delay(2000);
            var roster = client.Processes;
            if (roster.Count == 0 || roster.Count(x => x.IsRunning) != client.ActiveProcesses
                || !roster.Any(x => x.IsRoot && x.Process.Id == root.Id && x.Process.StartTimeUtcTicks == root.StartTimeUtcTicks))
                throw new InvalidOperationException("Collector roster is missing the target or tracked descendants.");
            MonitorTabs.SelectedIndex = 1;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (MonitoredProcessGrid.Items.Count != roster.Count) throw new InvalidOperationException("Collector process roster did not reach the bound grid.");
            passed($"PASS: live process roster shows {roster.Count} tracked processes, including the original target, and agrees with collector count");
            MonitorTabs.SelectedIndex = 0;
            KindFilter.SelectedIndex = 2;
            await Task.Delay(2000);
            EventSearch.Text = "127.0.0.1";
            await Task.Delay(2000);
            EventSearch.Clear();
            KindFilter.SelectedIndex = 0;
            activityView.SortDescriptions.Add(new SortDescription(nameof(Activity.Sequence), ListSortDirection.Descending));
            if (!activityView.IsEmpty) EventGrid.SelectedItem = activityView.Cast<Activity>().First();
            await Task.Delay(2000);
        }
        finally
        {
            await StopAsync();
            timer.Stop();
            while (client.TryTake(out var remaining)) AppendActivityBatch([remaining!]);
        }
        if (!string.IsNullOrEmpty(client.Error)) throw new InvalidOperationException(client.Error);
        if (client.Total <= 1) throw new InvalidOperationException("The target produced no activity during the probe.");
        if (activities.Count == 0 || EventGrid.Items.Count == 0) throw new InvalidOperationException("No live events reached the DataGrid.");
        passed($"PASS: real timer, grid, category/search filters, sort and selection remained responsive; {client.Total} events captured, {activities.Count} rows retained");
        if (ProcessCatalog.Get(root.Id).StartTimeUtcTicks != root.StartTimeUtcTicks) throw new InvalidOperationException("Target identity changed during probe.");
        passed("PASS: original target process is still running with the same creation time");
        passed("Evidence: " + client.JournalPath);
        // Retain the stopped client so normal window closing also exercises synchronous cleanup.
    }

    internal async Task CloseForDiagnosticsAsync()
    {
        preview = false;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Closed += (_, _) => closed.TrySetResult();
        Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
