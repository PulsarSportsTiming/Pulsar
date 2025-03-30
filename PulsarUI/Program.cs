using System;
using Avalonia;
using Avalonia.ReactiveUI;
using Microsoft.Extensions.DependencyInjection;
using PulsarUI.Interfaces;
using PulsarUI.Services;
using PulsarUI.ViewModels;
using PulsarUI.Views;

namespace PulsarUI;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Set up Dependency Injection and pass it into Avalonia
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI();

        // Skip dependency injection in design mode
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            return builder;
        }
        
        var services = new ServiceCollection();
        
        // Register services and view models here
        services.AddSingleton<IDatabaseService>(provider => new DatabaseService("Data Source=/home/david/PulsarDB.db"));
        services.AddSingleton<MainWindowViewModel>(); // Add the ViewModel to DI container
        services.AddSingleton<MainWindow>();
        
        // Build the ServiceProvider
        var serviceProvider = services.BuildServiceProvider();

        // Now configure Avalonia app to use DI
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI()
            .AfterSetup(_ =>
            {
                // Set the ServiceProvider for Avalonia
                var app = (App)Application.Current;
                app.ServiceProvider = serviceProvider;
            });
    }
}