using CommunityToolkit.Mvvm.ComponentModel;
using PulsarUI.Services;

namespace PulsarUI.Models;

public partial class IncrementalSpeed : ObservableObject
{
    // Speed stored as meters per second
    [ObservableProperty] private decimal _metersPerSecond;
    [ObservableProperty] private DownTrackInput? _input;

    // Raw value (m/s)
    public decimal RawValue => MetersPerSecond;

    // Backwards-compatible: formatted value (includes unit) e.g. "36.5 km/h"
    public string Value => TimingLabelHelpers.FormatSpeed(MetersPerSecond);

    // Numeric-only formatted value
    public string ValueNumeric => TimingLabelHelpers.FormatSpeedNumeric(MetersPerSecond);
}