using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MemCleaner;

internal static unsafe class StandaloneNotification
{
    private const uint IconId = 1;
    private const uint FallbackDisplaySeconds = 5;
    private const uint MaxDisplaySeconds = 30;
    private const int MillisecondsPerSecond = 1000;
    private const int DisplayMarginMs = 1000;

    public static void Show(string title, string text, bool isError)
    {
        var hwnd = PInvoke.CreateWindowEx(0, PInvoke.WC_STATIC, TrayApp.AppName, 0, 0, 0, 0, 0, HWND.HWND_MESSAGE, null,
            null, null);
        if (hwnd.IsNull)
            return;

        try
        {
            var data = new NOTIFYICONDATAW
            {
                cbSize = (uint)sizeof(NOTIFYICONDATAW),
                hWnd = hwnd,
                uID = IconId,
                uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP |
                         NOTIFY_ICON_DATA_FLAGS.NIF_INFO,
                hIcon = PInvoke.LoadIcon(default, isError ? PInvoke.IDI_ERROR : PInvoke.IDI_WARNING),
                dwInfoFlags = (isError ? NOTIFY_ICON_INFOTIP_FLAGS.NIIF_ERROR : NOTIFY_ICON_INFOTIP_FLAGS.NIIF_WARNING)
                              | NOTIFY_ICON_INFOTIP_FLAGS.NIIF_NOSOUND
            };
            TrayIcon.SetText(data.szTip.AsSpan(), TrayApp.AppName);
            TrayIcon.SetText(data.szInfoTitle.AsSpan(), title);
            TrayIcon.SetText(data.szInfo.AsSpan(), text);

            if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, in data))
                return;

            Thread.Sleep(DisplayMilliseconds());
            _ = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
        }
        finally
        {
            _ = PInvoke.DestroyWindow(hwnd);
        }
    }

    private static int DisplayMilliseconds()
    {
        uint seconds;
        if (!PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETMESSAGEDURATION, 0, &seconds, 0))
            seconds = FallbackDisplaySeconds;

        return (int)(Math.Min(seconds, MaxDisplaySeconds) * MillisecondsPerSecond) + DisplayMarginMs;
    }
}