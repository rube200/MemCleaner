using Windows.Win32.Security;
using Windows.Win32.System.SystemInformation;
using SystemInformationClass = Windows.Wdk.System.SystemInformation.SYSTEM_INFORMATION_CLASS;

namespace MemCleaner;

internal static unsafe class Memory
{
    private const int MegabyteShift = 20;
    private const ulong Gigabyte = 1UL << 30;

    private static readonly ulong PageSize = (ulong)Environment.SystemPageSize;

    public static MemoryStatus? GetStatus()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)sizeof(MEMORYSTATUSEX) };
        if (!PInvoke.GlobalMemoryStatusEx(ref status))
            return null;

        var (standby, modified) = GetPageLists();
        return new MemoryStatus(status.ullTotalPhys, status.ullAvailPhys, (int)status.dwMemoryLoad, standby, modified);
    }

    public static ulong PagesToBytes(nuint pages)
    {
        return pages * PageSize;
    }

    private static (ulong? Standby, ulong? Modified) GetPageLists()
    {
        MemoryListInformation lists;
        var status = Windows.Wdk.PInvoke.NtQuerySystemInformation(
            (SystemInformationClass)Native.SystemMemoryListInformation, &lists, (uint)sizeof(MemoryListInformation),
            null);
        if (status != NTSTATUS.STATUS_SUCCESS)
            return (null, null);

        nuint standby = 0;
        foreach (var pages in lists.PageCountByPriority)
            standby += pages;

        return (PagesToBytes(standby), PagesToBytes(lists.ModifiedPageCount));
    }

    public static string FormatBytes(ulong bytes)
    {
        return bytes >= Gigabyte
            ? $"{bytes / (double)Gigabyte:0.0} GB"
            : $"{bytes >> MegabyteShift} MB";
    }

    public static bool EnablePrivilege(string name)
    {
        HANDLE token;
        if (!PInvoke.OpenProcessToken(PInvoke.GetCurrentProcess(), TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES, &token))
            return false;

        try
        {
            if (!PInvoke.LookupPrivilegeValue(null, name, out var luid))
                return false;

            var privileges = new TOKEN_PRIVILEGES { PrivilegeCount = 1 };
            privileges.Privileges[0] = new LUID_AND_ATTRIBUTES
            {
                Luid = luid,
                Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED
            };

            return PInvoke.AdjustTokenPrivileges(token, false, &privileges, 0, null, null)
                   && (WIN32_ERROR)Marshal.GetLastPInvokeError() != WIN32_ERROR.ERROR_NOT_ALL_ASSIGNED;
        }
        finally
        {
            _ = PInvoke.CloseHandle(token);
        }
    }
}