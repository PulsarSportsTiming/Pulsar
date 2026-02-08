using System;
using Microsoft.Extensions.Configuration;

namespace PulsarUI.Services;

public static class AppSettings
{
    private static IConfigurationRoot? _config;

    static AppSettings()
    {
        LoadConfiguration();
    }

    public static void Initialize(IConfiguration configuration)
    {
        // Allow tests to inject a configuration
        _config = configuration as IConfigurationRoot ?? new ConfigurationBuilder().AddConfiguration(configuration).Build();
    }

    private static void LoadConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        _config = builder.Build();
    }

    public static string DistanceUnit => _config?[$"Units:Distance"] ?? "m";
    public static string SpeedUnit => _config?[$"Units:Speed"] ?? "m/s";

    public static int TimeResolution
    {
        get
        {
            if (int.TryParse(_config?[$"Resolution:Time"], out var v)) return v;
            return 4;
        }
    }

    public static int SpeedResolution
    {
        get
        {
            if (int.TryParse(_config?[$"Resolution:Speed"], out var v)) return v;
            return 2;
        }
    }

    // Returns a multiplication factor to convert a value in m/s into the configured speed unit.
    // For example, if target is km/h this returns 3.6 (m/s -> km/h). If target is mm/s returns 1000.
    public static decimal GetSpeedConversionFactor(string? speedUnit = null)
    {
        var unit = (speedUnit ?? SpeedUnit).Trim().ToLowerInvariant();

        // normalize common aliases
        return unit switch
        {
            "mm/s" or "mms" or "mmps" or "millimetre" or "millimetres" or "millimeter" or "millimeters" => 1000m,
            "m/s" or "mps" or "ms" or "meter" or "metre" or "meters" or "metres" => 1m,
            "km/h" or "kmh" or "kph" => 3.6m,
            "fps" or "ft/s" or "ftps" or "feetpersecond" or "feet/s" => 3.2808398950131m,
            "mph" or "milesperhour" or "miles/h" or "mi/h" => 2.2369362920544m,
            _ => 1m
        };
    }
}
