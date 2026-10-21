using System.Collections.Generic;

namespace MemCleaner;

internal sealed record CleanResult(
    MemoryStatus Before,
    MemoryStatus After,
    ulong? Combined,
    IReadOnlyList<string> Failures,
    TimeSpan Duration)
{
    private ulong InUseFreed => Drop(Before.InUse, After.InUse);

    private ulong? CacheCleared =>
        Before.Cached is { } before && After.Cached is { } after ? Drop(before, after) : null;

    public ulong TotalFreed => InUseFreed + (CacheCleared ?? 0);

    public string Title => Failures.Count == 0
        ? $"Memory cleaned in {Duration.TotalSeconds:0.0} s"
        : $"Memory partly cleaned in {Duration.TotalSeconds:0.0} s";

    public string Summary
    {
        get
        {
            var freed = $"In use −{Memory.FormatBytes(InUseFreed)}";
            if (CacheCleared is { } cache)
                freed += $" · Cache −{Memory.FormatBytes(cache)}";
            if (Combined is { } combined and > 0)
                freed += $" · Combined {Memory.FormatBytes(combined)}";

            var summary = $"{freed}\nRAM {Before.UsedPercent}% → {After.UsedPercent}%";
            return Failures.Count == 0 ? summary : $"{summary}\nFailed: {string.Join(", ", Failures)}";
        }
    }

    private static ulong Drop(ulong before, ulong after)
    {
        return before - Math.Min(before, after);
    }
}