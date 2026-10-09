using System.Diagnostics;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Report slow work and periodic refresh averages, with bounded logging. Timings measure this operation,
// not the entire game frame or all native damage callbacks.
internal readonly struct SlowOperation : IDisposable
{
    private static readonly Dictionary<string, long> NextReport = new();
    private static long _sampleUntil;
    private static int _sampleCount;
    private static double _sampleTotal, _sampleMax, _sampleAllocated;
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
        var now = Environment.TickCount64;
        var kilobytes = (GC.GetAllocatedBytesForCurrentThread() - _allocated) / 1024d;
        if (_name == "stats refresh")
        {
            if (_sampleUntil == 0) _sampleUntil = now + 10000;
            _sampleCount++; _sampleTotal += milliseconds; _sampleMax = Math.Max(_sampleMax, milliseconds);
            _sampleAllocated += kilobytes;
            if (now >= _sampleUntil)
            {
                BattleStatisticsPlugin.Logger.LogInfo($"Overlay sample: stats refresh {_sampleCount} calls, "
                    + $"average {_sampleTotal / _sampleCount:F1} ms, max {_sampleMax:F1} ms, "
                    + $"average allocated {_sampleAllocated / _sampleCount:F0} KiB.");
                _sampleUntil = now + 10000;
                _sampleCount = 0; _sampleTotal = _sampleMax = _sampleAllocated = 0;
            }
        }
        if (milliseconds < 12) return;
        if (now < NextReport.GetValueOrDefault(_name)) return;
        NextReport[_name] = now + 10000;
        BattleStatisticsPlugin.Logger.LogWarning($"Overlay timing: {_name} {milliseconds:F1} ms, allocated {kilobytes:F0} KiB, "
            + $"managed collections {GC.CollectionCount(0) - _collections}.");
    }
}
