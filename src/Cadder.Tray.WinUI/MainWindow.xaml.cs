using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Cadder.Tray.WinUI;

public sealed partial class MainWindow : Window
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private readonly nint _windowHandle;

    public MainWindow()
    {
        InitializeComponent();

        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.Closing += OnAppWindowClosing;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        RootFrame.Navigate(typeof(MainPage));
    }

    public void ShowAndActivate()
    {
        ShowWindow(_windowHandle, SwShow);
        Activate();
    }

    private void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        ShowWindow(_windowHandle, SwHide);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(nint windowHandle, int commandShow);
}
