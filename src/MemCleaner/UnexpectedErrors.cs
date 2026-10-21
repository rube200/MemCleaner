namespace MemCleaner;

internal static class UnexpectedErrors
{
    private const long QuietMs = 5 * 60 * 1000;

    private static string? _lastKey;
    private static string? _lastMessage;
    private static long _lastReportedMs;
    private static int _heldBack;

    public static void Report(Exception ex)
    {
        var key = $"{ex.GetType().FullName}: {ex.Message}";
        var now = Environment.TickCount64;
        if (key == _lastKey && now - _lastReportedMs < QuietMs)
        {
            _heldBack++;
            return;
        }

        var note = _heldBack == 0 ? ""
            : key == _lastKey ? $"\n(It also happened {_heldBack} more times since the last report.)"
            : $"\n(Before this, \"{_lastMessage}\" happened {_heldBack} more times.)";

        _lastKey = key;
        _lastMessage = ex.Message;
        _lastReportedMs = now;
        _heldBack = 0;
        ProblemReporter.Report("Unexpected error", ex.Message + note, true, ex + note);
    }
}