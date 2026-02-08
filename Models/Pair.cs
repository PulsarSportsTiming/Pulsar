using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PulsarUI.Models;

public partial class Pair : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private Category? _category;
    [ObservableProperty] private Run[] _runs = [];
    [ObservableProperty] private RunMode? _runMode;
    [ObservableProperty] private long _runStartTimeNs;

    // Cached DateTime (UTC) for convenience. This will be serialized alongside the canonical nanoseconds value.

    // Public read-only accessor for consumers that want the human-friendly DateTime (UTC).
    // This is intentionally serialized so MQTT consumers receive an ISO timestamp (UTC, with Z suffix).
    public DateTime? RunStartTime { get; private set; }

    // Called by the source-generated property setter for _runStartTimeNs when it's assigned.
    partial void OnRunStartTimeNsChanged(long value)
    {
        RunStartTime = ConvertNsToUtcDateTime(value);
        OnPropertyChanged(nameof(RunStartTime));
    }

    private static DateTime? ConvertNsToUtcDateTime(long ns)
    {
        if (ns <= 0) return null;
        // Seconds and remainder nanoseconds
        long seconds = ns / 1_000_000_000L;
        long remainderNs = ns % 1_000_000_000L;
        // Convert remainder nanoseconds to ticks (1 tick = 100 ns)
        long extraTicks = remainderNs / 100L;
        var dto = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        // Add ticks (keeps Kind==Utc)
        try
        {
            return dto.AddTicks(extraTicks);
        }
        catch
        {
            // Overflow guard
            return dto;
        }
    }
}