using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MobileMapper.App;

public sealed class MainWindow : Window
{
    public MainWindow()
    {
        Title = "MobileMapper — Phase 1 developer build";
        Content = new TextBlock { Text = "MobileMapper: Windows foundation", Margin = new Thickness(24) };
    }
}
