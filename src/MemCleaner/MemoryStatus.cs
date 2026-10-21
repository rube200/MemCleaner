namespace MemCleaner;

internal readonly record struct MemoryStatus(
    ulong Total,
    ulong Available,
    int UsedPercent,
    ulong? Standby,
    ulong? Modified)
{
    public ulong Used => Total - Available;

    public ulong InUse => Modified is { } modified ? Used - Math.Min(Used, modified) : Used;

    public ulong? Cached => Standby + Modified;
}