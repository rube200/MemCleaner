using System.Collections.Generic;
using System.Diagnostics;

namespace MemCleaner;

internal static unsafe class Cleaner
{
    private const uint NoCacheLimitFlags = 0;

    private static readonly nuint FlushFileCache = nuint.MaxValue;

    public static bool EnablePrivileges()
    {
        var profile = Memory.EnablePrivilege(Native.SeProfileSingleProcessName);
        var quota = Memory.EnablePrivilege(PInvoke.SE_INCREASE_QUOTA_NAME);
        return profile && quota;
    }

    public static CleanResult Clean()
    {
        var started = Stopwatch.GetTimestamp();
        var before = Memory.GetStatus() ?? default;
        ulong? combined = null;

        (string Name, Func<string?> Run)[] steps =
        [
            ("working sets", () => RunMemoryListCommand(Native.MemoryEmptyWorkingSets)),
            ("system cache", FlushSystemFileCache),
            ("modified list", () => RunMemoryListCommand(Native.MemoryFlushModifiedList)),
            ("standby list", () => RunMemoryListCommand(Native.MemoryPurgeStandbyList)),
            ("page combining", () => CombinePages(out combined)),
            ("registry cache", FlushRegistryCache)
        ];

        var failures = new List<string>();
        foreach (var (name, run) in steps)
            if (run() is { } problem)
                failures.Add($"{name} ({problem})");

        return new CleanResult(before, Memory.GetStatus() ?? default, combined, failures,
            Stopwatch.GetElapsedTime(started));
    }

    private static string? RunMemoryListCommand(int command)
    {
        return SetSystemInformation(Native.SystemMemoryListInformation, &command, sizeof(int));
    }

    private static string? FlushSystemFileCache()
    {
        return PInvoke.SetSystemFileCacheSize(FlushFileCache, FlushFileCache, NoCacheLimitFlags)
            ? null
            : DescribeWin32((WIN32_ERROR)Marshal.GetLastPInvokeError());
    }

    private static string? CombinePages(out ulong? combined)
    {
        var info = new MemoryCombineInformationEx();
        var problem = SetSystemInformation(Native.SystemCombinePhysicalMemoryInformation, &info,
            (uint)sizeof(MemoryCombineInformationEx));
        combined = problem is null ? Memory.PagesToBytes(info.PagesCombined) : null;
        return problem;
    }

    private static string? FlushRegistryCache()
    {
        return SetSystemInformation(Native.SystemRegistryReconciliationInformation, null, 0);
    }

    private static string? SetSystemInformation(int informationClass, void* data, uint size)
    {
        var status = (NTSTATUS)Native.NtSetSystemInformation(informationClass, data, size);
        return status == NTSTATUS.STATUS_SUCCESS ? null : DescribeNtStatus(status);
    }

    private static string DescribeNtStatus(NTSTATUS status)
    {
        return status == NTSTATUS.STATUS_PRIVILEGE_NOT_HELD ? "privilege not held"
            : status == NTSTATUS.STATUS_ACCESS_DENIED ? "access denied"
            : status == NTSTATUS.STATUS_NOT_SUPPORTED ? "not supported"
            : $"0x{(uint)status:X8}";
    }

    private static string DescribeWin32(WIN32_ERROR error)
    {
        return error switch
        {
            WIN32_ERROR.ERROR_PRIVILEGE_NOT_HELD => "privilege not held",
            WIN32_ERROR.ERROR_ACCESS_DENIED => "access denied",
            _ => $"error {(uint)error}"
        };
    }
}