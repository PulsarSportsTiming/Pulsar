using CommunityToolkit.Mvvm.ComponentModel;
using PulsarUI.Services;

namespace PulsarUI.Models;

public partial class IncrementalSpeed : ObservableObject
{
    [ObservableProperty] private long _deltaNs;
    [ObservableProperty] private SpeedTrap? _trap;

    public decimal RawValue
    {
        get
        {
            if (Trap == null)
                return 0m;

            var distanceMm = Trap.EndMm - Trap.StartMm;
            if (distanceMm <= 0 || DeltaNs <= 0)
                return 0m;

            var distanceMeters = distanceMm / 1000m;
            var timeSeconds = DeltaNs / 1_000_000_000m;
            return distanceMeters / timeSeconds; // Raw value in m/s
        }
    }

    // Backwards-compatible: formatted value (numeric-only, no unit)
    public string Value => TimingLabelHelpers.FormatSpeedNumeric(RawValue);
}