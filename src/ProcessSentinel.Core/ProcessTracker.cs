namespace ProcessSentinel.Core;

// Accessed by the ETW consumer; the lock also protects statistics and initial snapshot seeding.
public sealed class ProcessTracker
{
    private readonly Dictionary<int, ProcessInfo> active = new();
    private readonly object gate = new();
    private readonly bool includeChildren;
    public ProcessTracker(ProcessInfo root, bool includeChildren)
    {
        this.includeChildren = includeChildren;
        active[root.Id] = root;
    }
    public int Count { get { lock (gate) return active.Count; } }
    public ProcessInfo? Find(int id) { lock (gate) return active.GetValueOrDefault(id); }
    public void Seed(IEnumerable<ProcessInfo> snapshot)
    {
        if (!includeChildren) return;
        var pending = snapshot.ToList();
        bool changed;
        do
        {
            changed = false;
            foreach (var item in pending.ToArray())
                if (Start(item)) { pending.Remove(item); changed = true; }
        } while (changed);
    }
    public bool Start(ProcessInfo process)
    {
        lock (gate)
        {
            // A start event is authoritative: never retain a terminated identity after PID reuse.
            if (active.TryGetValue(process.Id, out var prior))
            {
                if (prior.StartTimeUtcTicks == process.StartTimeUtcTicks) return false;
                active.Remove(process.Id);
            }
            if (!includeChildren || !active.TryGetValue(process.ParentId, out var parent)) return false;
            if (process.StartTimeUtcTicks <= 0 || process.StartTimeUtcTicks < parent.StartTimeUtcTicks) return false;
            active[process.Id] = process;
            return true;
        }
    }
    public ProcessInfo? Stop(int id) { lock (gate) { active.Remove(id, out var process); return process; } }
}
