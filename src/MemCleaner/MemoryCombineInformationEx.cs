namespace MemCleaner;

// Win32: MEMORY_COMBINE_INFORMATION_EX
[StructLayout(LayoutKind.Sequential)]
internal struct MemoryCombineInformationEx
{
    public nint Handle;
    public nuint PagesCombined;
    public uint Flags;
}