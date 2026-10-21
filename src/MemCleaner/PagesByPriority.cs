using System.Runtime.CompilerServices;

namespace MemCleaner;

[InlineArray(8)]
internal struct PagesByPriority
{
    private nuint _element;
}