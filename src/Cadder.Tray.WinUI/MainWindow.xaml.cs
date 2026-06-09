using Microsoft.UI.Xaml;

namespace Cadder.Tray.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        RootFrame.Navigate(typeof(MainPage));
    }
}
