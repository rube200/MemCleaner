using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MemCleaner;

internal static unsafe class TrayApp
{
    public const string AppName = "MemCleaner";
    private const string WindowClass = AppName + "Tray";
    private const string TaskbarCreatedMessage = "TaskbarCreated";

    private const uint CleanDone = PInvoke.WM_APP + 2;
    private const uint RemoteClean = PInvoke.WM_APP + 3;
    private const uint KeySelect = PInvoke.NIN_SELECT | PInvoke.NINF_KEY;
    private const nint LowWordMask = 0xFFFF;
    private const int HighWordShift = 16;

    private const nuint RefreshTimer = 1;
    private const uint RefreshIntervalMs = 3000;

    private const int SignalAttempts = 20;
    private const int SignalRetryMs = 100;

    private const int HotkeyId = 1;
    private const HOT_KEY_MODIFIERS HotkeyModifiers = HOT_KEY_MODIFIERS.MOD_CONTROL | HOT_KEY_MODIFIERS.MOD_ALT;
    private const uint HotkeyKey = 'M';
    private const string HotkeyName = "Ctrl+Alt+M";

    private const long MillisecondsPerMinute = 60_000;
    private const long MinutesPerHour = 60;

    private static HWND _hwnd;
    private static MemoryStatus _lastStatus;
    private static bool _startWithWindows;
    private static uint _taskbarCreated;
    private static bool _hotkeyRegistered;
    private static (long AtMs, ulong Freed)? _lastClean;

    public static bool SignalRunningInstance()
    {
        var hwnd = PInvoke.FindWindow(WindowClass);
        return !hwnd.IsNull && PInvoke.PostMessage(hwnd, RemoteClean, default, default);
    }

    public static int SignalStartingInstance()
    {
        for (var attempt = 0; attempt < SignalAttempts; attempt++)
        {
            Thread.Sleep(SignalRetryMs);
            if (SignalRunningInstance())
                return 0;
        }

        ProblemReporter.Report("Not cleaned", $"Another {AppName} is running but didn't respond.", false);
        return 1;
    }

    public static int Run()
    {
        _ = PInvoke.CoInitializeEx(COINIT.COINIT_APARTMENTTHREADED);
        if (CreateWindow() is { } problem)
        {
            ProblemReporter.Report("Could not start", problem, true);
            return 1;
        }

        var warnings = Start();
        if (warnings.Count > 0)
            ProblemReporter.Report(AppName, string.Join("\n", warnings), false);

        while (PInvoke.GetMessage(out var msg, default, 0, 0) > 0)
        {
            _ = PInvoke.TranslateMessage(in msg);
            PInvoke.DispatchMessage(in msg);
        }

        return 0;
    }

    private static string? CreateWindow()
    {
        HINSTANCE instance = PInvoke.GetModuleHandle(default(PCWSTR));
        fixed (char* className = WindowClass)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = instance,
                lpszClassName = className
            };
            if (PInvoke.RegisterClassEx(in wc) == 0)
                return $"Could not register the tray window (error {Marshal.GetLastPInvokeError()}).";

            fixed (char* windowName = AppName)
            {
                _hwnd = PInvoke.CreateWindowEx(0, className, windowName, 0, 0, 0, 0, 0, default, default, instance,
                    null);
            }
        }

        return _hwnd.IsNull ? $"Could not create the tray window (error {Marshal.GetLastPInvokeError()})." : null;
    }

    private static List<string> Start()
    {
        var warnings = new List<string>();
        TrayIcon.Attach(_hwnd);

        _taskbarCreated = PInvoke.RegisterWindowMessage(TaskbarCreatedMessage);
        var taskbarFilterError = AllowMessage(_taskbarCreated);
        var trayFilterError = AllowMessage(TrayIcon.CallbackMessage);
        var remoteFilterError = AllowMessage(RemoteClean);
        if ((taskbarFilterError ?? trayFilterError ?? remoteFilterError) is { } filterError)
            warnings.Add(
                $"Windows refused tray messages (error {filterError}); right-click and relaunch-to-clean may not work.");

        _hotkeyRegistered =
            PInvoke.RegisterHotKey(_hwnd, HotkeyId, HotkeyModifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT, HotkeyKey);
        if (!_hotkeyRegistered)
            warnings.Add($"{HotkeyName} is already used by another app. Right-click the tray icon to clean.");

        if (!Cleaner.EnablePrivileges())
            warnings.Add("Could not enable the privileges needed to clean memory; cleaning may fail.");

        Refresh();
        if (PInvoke.SetTimer(_hwnd, RefreshTimer, RefreshIntervalMs, null) == 0)
            warnings.Add(
                $"Could not start the refresh timer (error {Marshal.GetLastPInvokeError()}); the tray icon won't update.");

        if (ProblemReporter.RegisterEventSource() is { } eventSourceError)
            warnings.Add($"Could not register the Event Log source: {eventSourceError}");

        try
        {
            (_startWithWindows, var locationWarning) = StartupTask.CheckAtStartup();
            if (locationWarning is not null)
                warnings.Add(locationWarning);
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not check Start with Windows: {ex.Message}");
        }

        return warnings;
    }

    private static int? AllowMessage(uint message)
    {
        return PInvoke.ChangeWindowMessageFilterEx(_hwnd, message, WINDOW_MESSAGE_FILTER_ACTION.MSGFLT_ALLOW)
            ? null
            : Marshal.GetLastPInvokeError();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (HandleMessage(msg, wParam, lParam))
                return default;
        }
        catch (Exception ex)
        {
            UnexpectedErrors.Report(ex);
        }

        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static bool HandleMessage(uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case TrayIcon.CallbackMessage:
                OnTrayEvent(wParam, lParam);
                return true;

            case PInvoke.WM_DPICHANGED:
            case PInvoke.WM_DISPLAYCHANGE:
            case PInvoke.WM_SETTINGCHANGE:
                TrayIcon.InvalidatePicture();
                Refresh();
                return false;

            case PInvoke.WM_HOTKEY:
            case RemoteClean:
                StartClean();
                return true;

            case PInvoke.WM_TIMER:
                Refresh();
                return true;

            case CleanDone:
                OnCleanDone();
                return true;

            case PInvoke.WM_DESTROY:
                OnDestroy();
                return true;
        }

        if (msg != _taskbarCreated || msg == 0)
            return false;

        TrayIcon.ExplorerRestarted();
        Refresh();
        return true;
    }
    
    private static void OnTrayEvent(WPARAM wParam, LPARAM lParam)
    {
        switch ((uint)(lParam.Value & LowWordMask))
        {
            case PInvoke.WM_CONTEXTMENU or KeySelect when TrayIcon.ModernCallbacks:
            {
                var anchor = unchecked((uint)wParam.Value);
                ShowMenu(unchecked((short)anchor), unchecked((short)(anchor >> HighWordShift)));
                break;
            }
            case PInvoke.WM_RBUTTONUP when !TrayIcon.ModernCallbacks && PInvoke.GetCursorPos(out var cursor):
                ShowMenu(cursor.X, cursor.Y);
                break;
        }
    }

    private static void ShowMenu(int x, int y)
    {
        var command = TrayMenu.Show(_hwnd, x, y, CleanJob.IsRunning, _startWithWindows);
        switch (command)
        {
            case MenuCommand.None:
                break;
            case MenuCommand.Clean:
                StartClean();
                break;
            case MenuCommand.StartWithWindows:
                ToggleStartWithWindows();
                break;
            case MenuCommand.Exit:
                if (!PInvoke.DestroyWindow(_hwnd))
                    ProblemReporter.Report("Could not exit",
                        $"Closing the tray window failed (error {Marshal.GetLastPInvokeError()}).", true);
                break;
            default:
                throw new InvalidOperationException($"Unknown menu command {(int)command}.");
        }
    }

    private static void StartClean()
    {
        if (CleanJob.TryStart(_hwnd, CleanDone))
            Refresh();
    }

    private static void OnCleanDone()
    {
        var (result, exception) = CleanJob.Finish();
        if (result is not null)
            _lastClean = (Environment.TickCount64, result.TotalFreed);
        Refresh();

        if (result is null)
            ProblemReporter.Report("Memory clean failed", exception?.Message ?? "Unknown error", true,
                exception?.ToString());
        else if (result.Failures.Count == 0)
            _ = TrayIcon.TryShowBalloon(result.Title, result.Summary, NOTIFY_ICON_INFOTIP_FLAGS.NIIF_INFO);
        else
            ProblemReporter.Report(result.Title, result.Summary, false);
    }

    private static void ToggleStartWithWindows()
    {
        if (!_startWithWindows && StartupTask.LocationProblem() is { } locationProblem)
        {
            ProblemReporter.Report("Can't start with Windows from here", locationProblem, false);
            return;
        }

        try
        {
            if (_startWithWindows)
                StartupTask.Disable();
            else
                StartupTask.Enable();
            _startWithWindows = !_startWithWindows;
        }
        catch (Exception ex)
        {
            ProblemReporter.Report("Could not change Start with Windows", ex.Message, true, ex.ToString());
        }
    }

    private static void OnDestroy()
    {
        if (_hotkeyRegistered)
            _ = PInvoke.UnregisterHotKey(_hwnd, HotkeyId);
        TrayIcon.Remove();
        PInvoke.PostQuitMessage(0);
    }

    private static void Refresh()
    {
        var status = Memory.GetStatus() ?? _lastStatus;
        _lastStatus = status;
        TrayIcon.Update(status.UsedPercent, Tooltip(status));
    }

    private static string Tooltip(MemoryStatus status)
    {
        if (CleanJob.IsRunning)
            return $"{AppName} — cleaning…";

        var text =
            $"RAM {status.UsedPercent}% — {Memory.FormatBytes(status.Used)} / {Memory.FormatBytes(status.Total)}";
        if (status.Cached is { } cached)
            text += $"\nCache {Memory.FormatBytes(cached)}";
        if (_lastClean is { } lastClean)
            text += $"\nLast cleaned {TimeAgo(lastClean.AtMs)} (−{Memory.FormatBytes(lastClean.Freed)})";
        return text + (_hotkeyRegistered ? $"\n{HotkeyName} or right-click to clean" : "\nRight-click to clean");
    }

    private static string TimeAgo(long atMs)
    {
        var minutes = (Environment.TickCount64 - atMs) / MillisecondsPerMinute;
        return minutes switch
        {
            < 1 => "just now",
            < MinutesPerHour => $"{minutes} min ago",
            _ => $"{minutes / MinutesPerHour} h {minutes % MinutesPerHour} min ago"
        };
    }
}