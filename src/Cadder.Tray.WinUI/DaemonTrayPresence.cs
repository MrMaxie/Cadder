using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Cadder.Tray.WinUI;

internal sealed class DaemonTrayPresence : IDisposable
{
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint LrDefaultSize = 0x00000040;
    private static readonly nint ApplicationIconResource = 32512;
    private readonly nint _iconHandle;
    private readonly bool _ownsIcon;
    private readonly NotifyIconData _iconData;
    private bool _disposed;

    public DaemonTrayPresence(Window ownerWindow)
    {
        ArgumentNullException.ThrowIfNull(ownerWindow);

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(ownerWindow);
        (_iconHandle, _ownsIcon) = LoadIcon();

        _iconData = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = windowHandle,
            Id = 1,
            Flags = NifIcon | NifTip,
            IconHandle = _iconHandle,
            Tip = "Cadder daemon"
        };

        if (!ShellNotifyIcon(NimAdd, in _iconData))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to add the Cadder tray icon.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ShellNotifyIcon(NimDelete, in _iconData);
        if (_ownsIcon)
        {
            DestroyIcon(_iconHandle);
        }

        _disposed = true;
    }

    private static (nint Handle, bool OwnsHandle) LoadIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        var iconHandle = File.Exists(iconPath)
            ? LoadImage(0, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize)
            : 0;

        if (iconHandle != 0)
        {
            return (iconHandle, true);
        }

        return (LoadIcon(0, ApplicationIconResource), false);
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", SetLastError = true)]
    private static extern bool ShellNotifyIcon(uint message, in NotifyIconData data);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(
        nint instance,
        string name,
        uint type,
        int desiredWidth,
        int desiredHeight,
        uint load);

    [DllImport("user32.dll", EntryPoint = "LoadIconW", SetLastError = true)]
    private static extern nint LoadIcon(nint instance, nint iconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint icon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
    }
}
