using CommunityToolkit.Mvvm.ComponentModel;
using PulsarUI.Services;

namespace PulsarUI.Models;

public partial class ReactionTime : ObservableObject
{
    [ObservableProperty] private long _valueNs;
    [ObservableProperty] private InputRole _trigger;
    [ObservableProperty] private long _expectedReactionTimeNs;
    // Raw unadjusted trigger timestamp in nanoseconds (the detection event timestamp)
    [ObservableProperty] private long _triggerTimestampNanoseconds;

    // Raw value in seconds as decimal
    public decimal RawValue => (ValueNs - ExpectedReactionTimeNs) / 1_000_000_000m;

    // Backwards-compatible: formatted value (numeric-only, no unit)
    public string Value
    {
        get
        {
            var deltaNs = ValueNs - ExpectedReactionTimeNs;
            // Use the nanoseconds-based formatter to avoid any accidental unit mismatch
            var formatted = TimingLabelHelpers.FormatTimeFromNanosecondsNumeric(deltaNs);
            return deltaNs >= 0 ? "+" + formatted : formatted;
        }
    }
}