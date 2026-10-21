using System.IO;
using System.Security;
using Windows.Win32.System.EventLog;
using Windows.Win32.UI.Shell;
using Microsoft.Win32;

namespace MemCleaner;

internal static class ProblemReporter
{
    private const string LogFileName = "error.log";
    private const string OldLogFileName = "error.old.log";
    private const long MaxLogBytes = 1 << 20;
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";
    private const string LineSeparator = " | ";
    private const uint EventId = 1;
    private const ushort NoCategory = 0;
    private const int NoError = 0;

    private const string EventSourceKey = @"SYSTEM\CurrentControlSet\Services\EventLog\Application\" + TrayApp.AppName;
    private const string EventMessageFileName = "EventCreate.exe";
    private const string EventMessageFileValue = "EventMessageFile";
    private const string TypesSupportedValue = "TypesSupported";

    private const int SupportedEventTypes = (int)REPORT_EVENT_TYPE.EVENTLOG_ERROR_TYPE |
                                            (int)REPORT_EVENT_TYPE.EVENTLOG_WARNING_TYPE |
                                            (int)REPORT_EVENT_TYPE.EVENTLOG_INFORMATION_TYPE;

    private static readonly Lock LogLock = new();

    private static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), TrayApp.AppName);

    public static void Report(string title, string message, bool isError, string? details = null)
    {
        message = WithLogFailure(message, Log(title, details ?? message, isError));
        if (!TrayIcon.TryShowBalloon(title, message,
                isError ? NOTIFY_ICON_INFOTIP_FLAGS.NIIF_ERROR : NOTIFY_ICON_INFOTIP_FLAGS.NIIF_WARNING))
            StandaloneNotification.Show(title, message, isError);
    }

    public static void ReportWithoutTray(string title, string message, bool isError, string? details = null)
    {
        message = WithLogFailure(message, Log(title, details ?? message, isError));
        StandaloneNotification.Show(title, message, isError);
    }

    public static string? RegisterEventSource()
    {
        var messageFile = Path.Combine(Environment.SystemDirectory, EventMessageFileName);
        if (!File.Exists(messageFile))
            return $"{messageFile} not found";

        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(EventSourceKey);
            if (key.GetValue(EventMessageFileValue) as string != messageFile)
                key.SetValue(EventMessageFileValue, messageFile, RegistryValueKind.String);
            if (key.GetValue(TypesSupportedValue) is not SupportedEventTypes)
                key.SetValue(TypesSupportedValue, SupportedEventTypes, RegistryValueKind.DWord);
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            return ex.Message;
        }
    }

    private static bool Log(string title, string message, bool isError)
    {
        var text = $"{title}: {message}";

        var logFileError = WriteLogFile(text, isError);
        var eventLogText = logFileError is null ? text : $"{text}\nCould not write {LogFileName}: {logFileError}";
        var eventLogError = WriteEventLog(eventLogText, isError);

        if (eventLogError != NoError && logFileError is null)
            WriteLogFile($"Could not write to the Windows Event Log (error {eventLogError}).", false);

        return logFileError is null || eventLogError == NoError;
    }

    private static string WithLogFailure(string message, bool logged)
    {
        return logged ? message : $"{message}\n(The error log could not be written either.)";
    }

    private static string? WriteLogFile(string text, bool isError)
    {
        try
        {
            lock (LogLock)
            {
                var directory = LogDirectory;
                Directory.CreateDirectory(directory);

                var path = Path.Combine(directory, LogFileName);
                var file = new FileInfo(path);
                if (file is { Exists: true, Length: > MaxLogBytes })
                    File.Move(path, Path.Combine(directory, OldLogFileName), true);

                var level = isError ? "ERROR" : "WARN ";
                File.AppendAllText(path,
                    $"{DateTime.Now.ToString(TimestampFormat)} {level} {text.ReplaceLineEndings(LineSeparator)}{Environment.NewLine}");
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    private static int WriteEventLog(string text, bool isError)
    {
        using var source = PInvoke.RegisterEventSource(null, TrayApp.AppName);
        if (source.IsInvalid)
            return Marshal.GetLastPInvokeError();

        var type = isError ? REPORT_EVENT_TYPE.EVENTLOG_ERROR_TYPE : REPORT_EVENT_TYPE.EVENTLOG_WARNING_TYPE;
        return PInvoke.ReportEvent(source, type, NoCategory, EventId, default, [text])
            ? NoError
            : Marshal.GetLastPInvokeError();
    }
}