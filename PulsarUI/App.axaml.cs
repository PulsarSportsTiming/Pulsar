using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PulsarUI.Views;

namespace PulsarUI;

public partial class App : Application
{
    // Keep ServiceProvider nullable; it won't be set in design mode.
    public System.IServiceProvider? ServiceProvider { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Avalonia.Controls.Design.IsDesignMode)
            {
                // In design mode we don't have DI available; create a simple MainWindow instance so the designer can render.
                desktop.MainWindow = new MainWindow();
            }
            else if (ServiceProvider != null)
            {
                // Use DI to get the MainWindow at runtime
                desktop.MainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}