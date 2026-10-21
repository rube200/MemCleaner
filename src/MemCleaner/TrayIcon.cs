using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MemCleaner;

internal static unsafe class TrayIcon
{
    public const uint CallbackMessage = PInvoke.WM_APP + 1;

    private const uint IconId = 1;
    private const int NoIconPercent = -1;
    private const char Ellipsis = '…';

    private static HWND _owner;
    private static HICON _icon;
    private static int _iconPercent = NoIconPercent;
    private static bool _added;
    private static string? _tooltip;
    private static (string Title, string Text, NOTIFY_ICON_INFOTIP_FLAGS Flags)? _pendingBalloon;

    public static bool ModernCallbacks { get; private set; }

    public static void Attach(HWND owner)
    {
        _owner = owner;
    }

    public static void InvalidatePicture()
    {
        _iconPercent = NoIconPercent;
    }

    public static void ExplorerRestarted()
    {
        _added = false;
        InvalidatePicture();
    }

    public static void Update(int percent, string tooltip)
    {
        var pictureChanged = UpdatePicture(percent);
        if (_added && !pictureChanged && tooltip == _tooltip)
            return;

        var data = NewData(NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON |
                           NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
        SetText(data.szTip.AsSpan(), tooltip);

        var adding = !_added;
        if (!PInvoke.Shell_NotifyIcon(adding ? NOTIFY_ICON_MESSAGE.NIM_ADD : NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data))
            return;

        _added = true;
        _tooltip = tooltip;

        if (adding)
        {
            data.uVersion = PInvoke.NOTIFYICON_VERSION_4;
            ModernCallbacks = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, in data);
        }

        if (_pendingBalloon is not { } pending)
            return;

        _pendingBalloon = null;
        _ = TryShowBalloon(pending.Title, pending.Text, pending.Flags);
    }
    
    public static bool TryShowBalloon(string title, string text, NOTIFY_ICON_INFOTIP_FLAGS flags)
    {
        if (_owner.IsNull)
            return false;

        if (!_added)
        {
            _pendingBalloon = (title, text, flags);
            return true;
        }

        var data = NewData(NOTIFY_ICON_DATA_FLAGS.NIF_INFO);
        SetText(data.szInfoTitle.AsSpan(), title);
        SetText(data.szInfo.AsSpan(), text);
        data.dwInfoFlags = flags | NOTIFY_ICON_INFOTIP_FLAGS.NIIF_NOSOUND;
        _ = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data);
        return true;
    }
    
    public static void Remove()
    {
        _ = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, NewData(0));
        _added = false;
        _owner = default;
    }
    
    public static void SetText(Span<char> buffer, string text)
    {
        var capacity = buffer.Length - 1;
        if (text.Length <= capacity)
        {
            text.AsSpan().CopyTo(buffer);
            buffer[text.Length] = '\0';
            return;
        }

        text.AsSpan(0, capacity - 1).CopyTo(buffer);
        buffer[capacity - 1] = Ellipsis;
        buffer[capacity] = '\0';
    }

    private static bool UpdatePicture(int percent)
    {
        if (percent == _iconPercent && !_icon.IsNull)
            return false;

        var icon = IconRenderer.Create(percent, _owner);
        if (icon.IsNull)
            return false;

        if (!_icon.IsNull)
            _ = PInvoke.DestroyIcon(_icon);
        _icon = icon;
        _iconPercent = percent;
        return true;
    }

    private static NOTIFYICONDATAW NewData(NOTIFY_ICON_DATA_FLAGS flags)
    {
        return new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _owner,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon
        };
    }
}