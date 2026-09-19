using CommunityToolkit.Mvvm.ComponentModel;
using PulsarUI.Services;

namespace PulsarUI.Models;

public partial class IncrementalTime : ObservableObject
{
    [ObservableProperty] private long _valueNs;
    [ObservableProperty] private DownTrackInput? _input;

    // Raw absolute timestamp of the downtrack event (nanoseconds), used to compare
    // finish-line crossings across lanes on a shared clock. 0 = not set.
    [ObservableProperty] private long _timestampNanoseconds;

    // Raw value in seconds as decimal
    public decimal RawValue => ValueNs / 1_000_000_000m;

    // Backwards-compatible: formatted value (numeric-only, no unit)
    public string Value => TimingLabelHelpers.FormatTimeFromNanosecondsNumeric(ValueNs);
}