using Windows.Win32.UI.WindowsAndMessaging;

namespace MemCleaner;

internal static class TrayMenu
{
    private const uint MenuItemByCommand = 0;

    private const TRACK_POPUP_MENU_FLAGS MenuFlags = TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON |
                                                     TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD |
                                                     TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY;

    public static MenuCommand Show(HWND owner, int x, int y, bool cleaning, bool startWithWindows)
    {
        using var menu = PInvoke.CreatePopupMenu_SafeHandle();
        _ = PInvoke.AppendMenu(menu, cleaning ? MENU_ITEM_FLAGS.MF_GRAYED : MENU_ITEM_FLAGS.MF_STRING,
            (nuint)MenuCommand.Clean, cleaning ? "Cleaning…" : "Clean memory");
        _ = PInvoke.SetMenuDefaultItem(menu, (uint)MenuCommand.Clean, MenuItemByCommand);
        _ = PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0);
        _ = PInvoke.AppendMenu(menu, startWithWindows ? MENU_ITEM_FLAGS.MF_CHECKED : MENU_ITEM_FLAGS.MF_STRING,
            (nuint)MenuCommand.StartWithWindows, "Start with Windows");
        _ = PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, (nuint)MenuCommand.Exit, "Exit");

        _ = PInvoke.SetForegroundWindow(owner);
        int command = PInvoke.TrackPopupMenuEx(menu, (uint)MenuFlags, x, y, owner);
        _ = PInvoke.PostMessage(owner, PInvoke.WM_NULL, default, default);
        return (MenuCommand)command;
    }
}