using Windows.Win32.System.Com;

namespace MemCleaner;

internal readonly unsafe ref struct ComRef<T>(T* pointer) : IDisposable where T : unmanaged
{
    public T* Pointer => pointer;

    public void Dispose()
    {
        if (pointer != null)
            _ = ((IUnknown*)pointer)->Release();
    }
}