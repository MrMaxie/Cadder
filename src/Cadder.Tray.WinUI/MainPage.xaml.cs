using Microsoft.UI.Xaml.Controls;

namespace Cadder.Tray.WinUI;

public sealed partial class MainPage : Page
{
  public MainPage()
  {
    InitializeComponent();
  }

  private async void QuitDaemonButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
  {
    QuitDaemonButton.IsEnabled = false;

    if (Microsoft.UI.Xaml.Application.Current is App app)
    {
      await app.QuitDaemonAsync();
    }
  }
}
