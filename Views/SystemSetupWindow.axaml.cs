using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PulsarUI.Views;

public partial class SystemSetupWindow : Window
{
    public SystemSetupWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

