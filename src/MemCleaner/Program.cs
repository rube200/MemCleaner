using MemCleaner;

const string singleInstanceMutex = @"Local\MemCleaner.SingleInstance";

AppDomain.CurrentDomain.UnhandledException += static (_, e) =>
{
    var exception = e.ExceptionObject as Exception;
    TrayIcon.Remove();
    ProblemReporter.ReportWithoutTray("Unexpected error, closing", exception?.Message ?? "Unknown error", true,
        exception?.ToString());
};

if (TrayApp.SignalRunningInstance())
    return 0;

if (!Environment.IsPrivilegedProcess)
    return Elevation.RelaunchAsAdministrator();

using var singleInstance = new Mutex(true, singleInstanceMutex, out var isFirstInstance);
return isFirstInstance ? TrayApp.Run() : TrayApp.SignalStartingInstance();