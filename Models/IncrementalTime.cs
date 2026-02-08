using CommunityToolkit.Mvvm.ComponentModel;
using PulsarUI.Services;

namespace PulsarUI.Models;

public partial class IncrementalTime : ObservableObject
{
    [ObservableProperty] private long _valueNs;
    [ObservableProperty] private DownTrackInput? _input;

    // Raw value in seconds as decimal
    public decimal RawValue => ValueNs / 1_000_000_000m;

    // Backwards-compatible: formatted value (numeric-only, no unit)
    public string Value => TimingLabelHelpers.FormatTimeFromNanosecondsNumeric(ValueNs);
}