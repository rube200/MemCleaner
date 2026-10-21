namespace MemCleaner;

internal static unsafe partial class Native
{
    public const int SystemMemoryListInformation = 80;
    public const int SystemCombinePhysicalMemoryInformation = 130;
    public const int SystemRegistryReconciliationInformation = 155;

    public const int MemoryEmptyWorkingSets = 2;
    public const int MemoryFlushModifiedList = 3;
    public const int MemoryPurgeStandbyList = 4;

    public const string SeProfileSingleProcessName = "SeProfileSingleProcessPrivilege";

    [LibraryImport("ntdll.dll")]
    public static partial int NtSetSystemInformation(int systemInformationClass, void* systemInformation,
        uint systemInformationLength);
}