using System;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PulsarUI.Models;

public partial class Timestamp : ObservableObject
{
    [ObservableProperty] private string _senderIp = string.Empty;
    [ObservableProperty] private int _input;
    [ObservableProperty] private bool _direction;

    // store nanoseconds since Unix epoch (UTC)
    [ObservableProperty] private long _timestampNanoseconds;

    // Parse an ISO 8601 UTC timestamp (e.g. "2026-01-31T16:38:11.160620237Z")
    // into nanoseconds since Unix epoch. Assumes timestamp is >= epoch and
    // has at most 9 fractional digits (1 ns resolution).
    public void SetFromIso(string iso)
    {
        if (iso is null) throw new ArgumentNullException(nameof(iso));

        // Use DateTimeOffset for the whole-seconds / offset handling
        var dto = DateTimeOffset.Parse(iso);
        long seconds = dto.ToUnixTimeSeconds();

        // extract fractional digits (if any) and normalize to 9 digits => nanoseconds
        var fracMatch = Regex.Match(iso, @"\.(\d+)");
        int nanos = 0;
        if (fracMatch.Success)
        {
            var f = fracMatch.Groups[1].Value;
            if (f.Length > 9) f = f.Substring(0, 9); // user guaranteed no >1ns precision, but trim defensively
            f = f.PadRight(9, '0');
            nanos = int.Parse(f);
        }

        // set via property so change notifications run
        TimestampNanoseconds = seconds * 1_000_000_000L + nanos;
    }

    // Format the stored nanoseconds back to an ISO 8601 UTC string.
    // Emits fractional seconds only when non-zero, with up to 9 digits.
    public string ToIso()
    {
        long seconds = TimestampNanoseconds / 1_000_000_000L;
        int nanos = (int)(TimestampNanoseconds % 1_000_000_000L);

        var dto = DateTimeOffset.FromUnixTimeSeconds(seconds).ToUniversalTime();
        string basePart = dto.ToString("yyyy-MM-dd'T'HH:mm:ss");
        if (nanos == 0) return basePart + "Z";

        string frac = nanos.ToString("D9").TrimEnd('0');
        return basePart + "." + frac + "Z";
    }
}