using CommunityToolkit.Mvvm.ComponentModel;
using PulsarUI.Services;

namespace PulsarUI.Models;

public partial class ReactionTime : ObservableObject
{
    [ObservableProperty] private long _valueNs;
    [ObservableProperty] private InputRole _trigger;
    [ObservableProperty] private long _expectedReactionTimeNs;

    // Raw value in seconds as decimal
    public decimal RawValue => (ValueNs - ExpectedReactionTimeNs) / 1_000_000_000m;

    // Backwards-compatible: formatted value (numeric-only, no unit)
    public string Value => TimingLabelHelpers.FormatTimeFromNanosecondsNumeric(ValueNs - ExpectedReactionTimeNs);
}