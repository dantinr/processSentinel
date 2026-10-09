using System.ComponentModel;
using System.Diagnostics;
using ProcessSentinel.Core;
using Activity = ProcessSentinel.Core.Activity;

namespace ProcessSentinel.App;

internal sealed class MonitorSession(ProcessInfo root, bool includeChildren) : INotifyPropertyChanged
{
    public ProcessInfo Root { get; } = root;
    public bool IncludeChildren { get; } = includeChildren;
    public MonitorClient Client { get; } = new();
    public Stopwatch Duration { get; } = new();
    public Queue<Activity> Recent { get; } = new();
    public bool Starting { get; set; } = true;
    private string label = "";
    public string Label => label;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Retain(Activity activity)
    {
        Recent.Enqueue(activity);
        if (Recent.Count > 5000) Recent.Dequeue();
    }

    public void UpdateLabel(string? previewState = null)
    {
        string state = previewState ?? (Starting ? "连接中" : Client.Running ? "监控中" : string.IsNullOrEmpty(Client.Error) ? "已停止" : "异常");
        string next = $"{Root.Label} · {state}" + (previewState is null ? $" · {Client.Total:N0} 条" : "");
        if (label == next) return;
        label = next;
        PropertyChanged?.Invoke(this, new(nameof(Label)));
    }
}
