using System.ComponentModel;
using System.Diagnostics;

namespace MemCleaner;

internal static class Elevation
{
    private const string RunAsAdminVerb = "runas";

    public static int RelaunchAsAdministrator()
    {
        try
        {
            var path = Environment.ProcessPath ??
                       throw new InvalidOperationException("Could not find the path of this executable.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = RunAsAdminVerb })?.Dispose();
            return 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == (int)WIN32_ERROR.ERROR_CANCELLED)
        {
            ProblemReporter.Report("Not started", $"{TrayApp.AppName} needs administrator rights to clean memory.",
                false);
            return 1;
        }
        catch (Exception ex)
        {
            ProblemReporter.Report("Could not restart as administrator", ex.Message, true, ex.ToString());
            return 1;
        }
    }
}