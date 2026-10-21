namespace MemCleaner;

// Win32: SYSTEM_MEMORY_LIST_INFORMATION
[StructLayout(LayoutKind.Sequential)]
internal struct MemoryListInformation
{
    public nuint ZeroPageCount;
    public nuint FreePageCount;
    public nuint ModifiedPageCount;
    public nuint ModifiedNoWritePageCount;
    public nuint BadPageCount;
    public PagesByPriority PageCountByPriority;
    public PagesByPriority RepurposedPagesByPriority;
    public nuint ModifiedPageCountPageFile;
}