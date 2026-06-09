using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Cadder.Tray.WinUI;

internal sealed class DaemonTrayPresence : IDisposable
{
  private const uint NimAdd = 0x00000000;
  private const uint NimDelete = 0x00000002;
  private const uint NifMessage = 0x00000001;
  private const uint NifIcon = 0x00000002;
  private const uint NifTip = 0x00000004;
  private const uint ImageIcon = 1;
  private const uint LrLoadFromFile = 0x00000010;
  private const uint LrDefaultSize = 0x00000040;
  private const uint WmApp = 0x8000;
  private const uint WmTrayIcon = WmApp + 17;
  private const uint WmContextMenu = 0x007B;
  private const uint WmLButtonUp = 0x0202;
  private const uint WmRButtonUp = 0x0205;
  private const nuint SubclassId = 1;
  private static readonly nint ApplicationIconResource = 32512;
  private readonly SubclassProc _subclassProc;
  private readonly nint _windowHandle;
  private readonly nint _iconHandle;
  private readonly bool _ownsIcon;
  private readonly NotifyIconData _iconData;
  private bool _disposed;

  public event EventHandler<TrayIconActivatedEventArgs>? Activated;

  public DaemonTrayPresence(Window ownerWindow)
  {
    ArgumentNullException.ThrowIfNull(ownerWindow);

    _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(ownerWindow);
    _subclassProc = TrayWindowSubclassProc;
    if (!SetWindowSubclass(_windowHandle, _subclassProc, SubclassId, 0))
    {
      throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to attach the Cadder tray icon callback.");
    }

    (_iconHandle, _ownsIcon) = LoadIcon();

    _iconData = new NotifyIconData
    {
      Size = (uint)Marshal.SizeOf<NotifyIconData>(),
      WindowHandle = _windowHandle,
      Id = 1,
      Flags = NifMessage | NifIcon | NifTip,
      CallbackMessage = WmTrayIcon,
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
    RemoveWindowSubclass(_windowHandle, _subclassProc, SubclassId);
    if (_ownsIcon)
    {
      DestroyIcon(_iconHandle);
    }

    _disposed = true;
  }

  private nint TrayWindowSubclassProc(
      nint windowHandle,
      uint message,
      nuint wParam,
      nint lParam,
      nuint subclassId,
      nuint referenceData)
  {
    if (message == WmTrayIcon)
    {
      var activationKind = (uint)lParam switch
      {
        WmLButtonUp => TrayIconActivationKind.LeftClick,
        WmRButtonUp or WmContextMenu => TrayIconActivationKind.RightClick,
        _ => (TrayIconActivationKind?)null
      };

      if (activationKind is not null)
      {
        Activated?.Invoke(this, new TrayIconActivatedEventArgs(activationKind.Value));
      }
    }

    return DefSubclassProc(windowHandle, message, wParam, lParam);
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

  [DllImport("comctl32.dll", SetLastError = true)]
  private static extern bool SetWindowSubclass(
      nint windowHandle,
      SubclassProc subclassProc,
      nuint subclassId,
      nuint referenceData);

  [DllImport("comctl32.dll", SetLastError = true)]
  private static extern bool RemoveWindowSubclass(
      nint windowHandle,
      SubclassProc subclassProc,
      nuint subclassId);

  [DllImport("comctl32.dll")]
  private static extern nint DefSubclassProc(
      nint windowHandle,
      uint message,
      nuint wParam,
      nint lParam);

  private delegate nint SubclassProc(
      nint windowHandle,
      uint message,
      nuint wParam,
      nint lParam,
      nuint subclassId,
      nuint referenceData);

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

internal enum TrayIconActivationKind
{
  LeftClick,
  RightClick
}

internal sealed class TrayIconActivatedEventArgs(TrayIconActivationKind activationKind) : EventArgs
{
  public TrayIconActivationKind ActivationKind { get; } = activationKind;
}
