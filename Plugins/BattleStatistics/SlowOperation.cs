using System.Diagnostics;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Only report slow work, with bounded logging. Timings measure this operation,
// not the entire game frame or all native damage callbacks.
internal readonly struct SlowOperation : IDisposable
{
    private static readonly Dictionary<string, long> NextReport = new();
    private readonly string _name;
    private readonly long _start, _allocated;
    private readonly int _collections;

    internal SlowOperation(string name)
    {
        _name = name;
        _start = Stopwatch.GetTimestamp();
        _allocated = GC.GetAllocatedBytesForCurrentThread();
        _collections = GC.CollectionCount(0);
    }

    public void Dispose()
    {
        var milliseconds = (Stopwatch.GetTimestamp() - _start) * 1000d / Stopwatch.Frequency;
        if (milliseconds < 12) return;
        var now = Environment.TickCount64;
        if (now < NextReport.GetValueOrDefault(_name)) return;
        NextReport[_name] = now + 10000;
        var kilobytes = (GC.GetAllocatedBytesForCurrentThread() - _allocated) / 1024d;
        BattleStatisticsPlugin.Logger.LogWarning($"Overlay timing: {_name} {milliseconds:F1} ms, allocated {kilobytes:F0} KiB, "
            + $"managed collections {GC.CollectionCount(0) - _collections}.");
    }
}
