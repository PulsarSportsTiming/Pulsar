using System;
using Avalonia;
using Avalonia.ReactiveUI;
using Microsoft.Extensions.Configuration;
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
        BuildAvaloniaApp(args)
            .StartWithClassicDesktopLifetime(args);
    }

    // Designer (and some tooling) expects a parameterless BuildAvaloniaApp method.
    // Provide an overload that delegates to the existing method so the designer can create an AppBuilder.
    public static AppBuilder BuildAvaloniaApp()
    {
        // Return a minimal AppBuilder for design-time tools (no DI or heavy configuration).
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI();
    }

    public static AppBuilder BuildAvaloniaApp(string[] args)
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

        // Ensure args is non-null for analyzers and downstream callers
        args ??= Array.Empty<string>();

        // Build configuration from appsettings.json, optional appsettings.local.json, environment variables, and command-line args
        var cfgBuilder = new ConfigurationBuilder();
        // BasePath: use the app's executable directory so a local appsettings.json can be found when running from the build output
        cfgBuilder.SetBasePath(AppContext.BaseDirectory);
        cfgBuilder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        // Load local overrides (not checked into source control). Later providers override earlier ones, so this will override appsettings.json when present.
        cfgBuilder.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
        cfgBuilder.AddEnvironmentVariables();
        cfgBuilder.AddCommandLine(args);
        var configuration = cfgBuilder.Build();

        var services = new ServiceCollection();

        // Determine connection string: config key is Database:ConnectionString
        // Environment variable can be set as Database__ConnectionString (double underscore) or just pass as command-line or in appsettings.json
        var connectionString = configuration.GetValue<string>("Database:ConnectionString") ?? string.Empty;

        // Fallback to the old embedded asset path if nothing provided
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Use a path relative to the app directory for the default DB asset
            var defaultPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "PulsarDB.db");
            connectionString = $"Data Source={defaultPath}";
        }

        // Register services and view models here
        var databaseServiceInstance = new DatabaseService(connectionString);
        services.AddSingleton<IDatabaseService>(databaseServiceInstance);
        services.AddSingleton<MainWindowViewModel>(); // Add the ViewModel to DI container
        services.AddSingleton<MainWindow>();

        // Register InputMapService using config/inputmap.json relative to app base directory
        // Match the project and repo `Config` folder (capitalized) so the file copied to output will be found
        var inputMapPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Config", "inputmap.json");
        services.AddSingleton(new InputMapService(inputMapPath));

        // Build the ServiceProvider
        var serviceProvider = services.BuildServiceProvider();

        // Now configure Avalonia app to use DI
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI()
            .AfterSetup(_ =>
            {
                // Safely set the ServiceProvider for Avalonia if Application.Current is available
                if (Application.Current is App app)
                {
                    app.ServiceProvider = serviceProvider;
                }
            });
    }
}