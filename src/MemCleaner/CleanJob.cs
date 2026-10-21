namespace MemCleaner;

internal static class CleanJob
{
    private static int _running;
    private static CleanResult? _result;
    private static Exception? _exception;

    public static bool IsRunning => Volatile.Read(ref _running) == 1;

    public static bool TryStart(HWND window, uint doneMessage)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return false;

        new Thread(() => Run(window, doneMessage)) { IsBackground = true, Name = "Cleaner" }.Start();
        return true;
    }

    public static (CleanResult? Result, Exception? Exception) Finish()
    {
        var result = Volatile.Read(ref _result);
        var exception = Volatile.Read(ref _exception);
        Volatile.Write(ref _running, 0);
        return (result, exception);
    }

    private static void Run(HWND window, uint doneMessage)
    {
        try
        {
            Volatile.Write(ref _result, Cleaner.Clean());
            Volatile.Write(ref _exception, null);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _result, null);
            Volatile.Write(ref _exception, ex);
        }

        if (PInvoke.PostMessage(window, doneMessage, default, default))
            return;

        var error = Marshal.GetLastPInvokeError();
        Volatile.Write(ref _running, 0);

        if (!PInvoke.IsWindow(window))
            return;

        ProblemReporter.ReportWithoutTray("Memory clean finished", $"The result could not be shown (error {error}).",
            false);
    }
}