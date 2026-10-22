using System.Collections.Generic;

namespace MemCleaner;

internal sealed record CleanResult(
    MemoryStatus Before,
    MemoryStatus After,
    IReadOnlyList<string> Failures,
    TimeSpan Duration)
{
    private ulong InUseFreed => Drop(Before.InUse, After.InUse);

    private ulong? CacheCleared =>
        Before.Cached is { } before && After.Cached is { } after ? Drop(before, after) : null;

    public ulong TotalFreed => InUseFreed + (CacheCleared ?? 0);

    public string Title => (Failures.Count == 0 ? "Freed" : "Partly cleaned: freed") +
                           $" {Memory.FormatBytes(TotalFreed)} in {Duration.TotalSeconds:0.0} s";

    public string Summary
    {
        get
        {
            var summary = $"RAM {Before.UsedPercent}% → {After.UsedPercent}%";
            if (CacheCleared is { } cache)
                summary += $"\nFrom apps {Memory.FormatBytes(InUseFreed)} · From file cache {Memory.FormatBytes(cache)}";

            return Failures.Count == 0 ? summary : $"{summary}\nFailed: {string.Join(", ", Failures)}";
        }
    }

    private static ulong Drop(ulong before, ulong after)
    {
        return before - Math.Min(before, after);
    }
}