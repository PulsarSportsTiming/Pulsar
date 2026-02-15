using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PulsarUI.Models;
using PulsarUI.Services;

namespace PulsarUI.Services
{
    public static class TimingLabelHelpers
    {
        // Internal helper for time formatting. includeUnit controls whether to append " s".
        private static string FormatTimeInternal(decimal seconds, bool includeUnit)
        {
            var rounded = Math.Round(seconds, AppSettings.TimeResolution, MidpointRounding.AwayFromZero);
            var s = rounded.ToString($"F{AppSettings.TimeResolution}", CultureInfo.InvariantCulture);
            return includeUnit ? s + " s" : s;
        }

        // New: format time values (seconds) using AppSettings.TimeResolution and append 's'
        public static string FormatTime(decimal seconds)
        {
            return FormatTimeInternal(seconds, true);
        }

        // New: numeric-only time (no unit)
        private static string FormatTimeNumeric(decimal seconds)
        {
            return FormatTimeInternal(seconds, false);
        }

        // New: convenience for nanoseconds input
        private static string FormatTimeFromNanoseconds(long nanoseconds)
        {
            return FormatTime(nanoseconds / 1_000_000_000m);
        }

        // New: numeric-only convenience for nanoseconds input
        public static string FormatTimeFromNanosecondsNumeric(long nanoseconds)
        {
            return FormatTimeNumeric(nanoseconds / 1_000_000_000m);
        }

        // New: nullable nanoseconds overload
        public static string FormatTimeNullable(long? nanoseconds)
        {
            return !nanoseconds.HasValue ? string.Empty : FormatTimeFromNanoseconds(nanoseconds.Value);
        }

        // New: nullable numeric-only nanoseconds overload
        public static string FormatTimeNullableNumeric(long? nanoseconds)
        {
            return !nanoseconds.HasValue ? string.Empty : FormatTimeFromNanosecondsNumeric(nanoseconds.Value);
        }

        // Internal helper for speed formatting. includeUnit controls whether to append the unit string.
        private static string FormatSpeedInternal(decimal metersPerSecond, bool includeUnit)
        {
            var factor = AppSettings.GetSpeedConversionFactor();
            var converted = metersPerSecond * factor;
            try
            {
                System.IO.File.AppendAllText("/tmp/pulsar_mqtt.log", DateTime.Now.ToString("o") + " FormatSpeedInternal: raw_mps=" + metersPerSecond + " factor=" + factor + " converted=" + converted + " unit=" + AppSettings.SpeedUnit + "\n");
            }
            catch { }
            var rounded = Math.Round(converted, AppSettings.SpeedResolution, MidpointRounding.AwayFromZero);
            var s = rounded.ToString($"F{AppSettings.SpeedResolution}", CultureInfo.InvariantCulture);
            return includeUnit ? s + " " + AppSettings.SpeedUnit : s;
        }

        // New: format speed values (meters per second) into configured unit using AppSettings.SpeedResolution
        public static string FormatSpeed(decimal metersPerSecond)
        {
            return FormatSpeedInternal(metersPerSecond, true);
        }

        // New: numeric-only speed (no unit)
        public static string FormatSpeedNumeric(decimal metersPerSecond)
        {
            return FormatSpeedInternal(metersPerSecond, false);
        }

        // Format a downtrack distance into the ET label according to selected distance unit.
        // Returns the ET-formatted label (e.g. "60' ET", "1/8 mi ET", "402.336m ET").
        // Supported distanceUnit values (case-insensitive): "m", "meters", "mi", "miles", "ft", "feet".
        public static string FormatDistanceLabel(int distanceMm, string distanceUnit)
        {
            var unit = string.IsNullOrWhiteSpace(distanceUnit) ? AppSettings.DistanceUnit : distanceUnit;
            if (distanceMm < 0) distanceMm = 0;
            unit = (unit ?? "m").Trim().ToLowerInvariant();
            
            

            switch (unit)
            {
                // exact fractional mile matches (strict mm equality)
                case "mi" or "miles" when distanceMm == 201168:
                    return "1/8 mi";
                case "mi" or "miles" when distanceMm == 402336:
                    return "1/4 mi";
                // default: show as feet with apostrophe
                case "mi" or "miles":
                case "ft" or "feet":
                {
                    var feet = (long)Math.Round(distanceMm / 304.8);
                    return $"{feet}";
                }
                // Support millimetres explicitly (raw mm value)
                case "mm":
                case "millimeters":
                case "millimetres":
                case "millimetre":
                    return $"{distanceMm}mm";
            }

            // default: meters
            var meters = distanceMm / 1000.0;
            var s = meters.ToString("F3", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
            return $"{s}m";
        }

        // Format a speed-trap label that matches the distance representation used by FormatDistanceLabel,
        // but without the " ET" suffix and with the speed unit appended (e.g. "60' mph", "1/8 mi mph", "402.336 km/h").
        // If speedUnit is null/empty the distance representation is returned (without ET).
        public static string FormatSpeedTrapLabel(int distanceMm, string distanceUnit, string speedUnit)
        {
            var distLabel = FormatDistanceLabel(distanceMm, distanceUnit);

            var unit = string.IsNullOrWhiteSpace(speedUnit) ? AppSettings.SpeedUnit : speedUnit;
            return string.IsNullOrWhiteSpace(unit) ? distLabel : $"{distLabel} {unit}";
        }

        // Generate per-lane timing label sequences from DownTrackInputs and SpeedTraps using strict mm equality for matching.
        // Returns a dictionary keyed by lane (InputLane.Left/InputLane.Right) with the ordered labels for that lane.
        // Each lane list contains: "Reaction Time", ET labels, any matching speed-trap labels immediately after the ET, then "Result".
        // If finishLineMm is specified (> 0), only labels up to and including that distance are included (before "Result").
        // Labels at the finish line distance are marked with IsBold = true.
        public static Dictionary<InputLane, List<TimingLabelItem>> GenerateTimingLabels(
            List<DownTrackInput>? inputs,
            List<SpeedTrap>? traps,
            string distanceUnit,
            string speedUnit,
            int finishLineMm = 0)
        {
            var result = new Dictionary<InputLane, List<TimingLabelItem>>();
            result[InputLane.Left] = new List<TimingLabelItem>();
            result[InputLane.Right] = new List<TimingLabelItem>();

            if (inputs == null) return result;

            var trapEnds = new HashSet<int>(traps?.Select(t => t.EndMm) ?? Enumerable.Empty<int>());

            var inputsByLane = inputs
                .GroupBy(i => i.Lane)
                .ToDictionary(g => g.Key, g => g.OrderBy(i => i.DistanceMm).ToList());

            foreach (var lane in Enum.GetValues<InputLane>())
            {
                var list = new List<TimingLabelItem> { new TimingLabelItem { Label = "Reaction Time", Value = string.Empty, IsBold = false } };
                var inputsForLane = inputsByLane.TryGetValue(lane, out List<DownTrackInput>? value) ? value : new List<DownTrackInput>();

                var remainingTrapEnds = new HashSet<int>(trapEnds);

                foreach (var inp in inputsForLane)
                {
                    // If a finish line is specified and this input exceeds it, skip remaining inputs
                    if (finishLineMm > 0 && inp.DistanceMm > finishLineMm)
                        break;

                    // Check if this input is at the finish line
                    bool isFinishLine = (finishLineMm > 0 && inp.DistanceMm == finishLineMm);

                    var etLabel = FormatDistanceLabel(inp.DistanceMm, distanceUnit) + " ET";
                    list.Add(new TimingLabelItem { Label = etLabel, Value = string.Empty, IsBold = isFinishLine });

                    if (!remainingTrapEnds.Contains(inp.DistanceMm)) continue;
                    var trapLabel = FormatSpeedTrapLabel(inp.DistanceMm, distanceUnit, speedUnit);
                    list.Add(new TimingLabelItem { Label = trapLabel, Value = string.Empty, IsBold = isFinishLine });
                    remainingTrapEnds.Remove(inp.DistanceMm);
                }

                list.Add(new TimingLabelItem { Label = "Result", Value = string.Empty, IsBold = false });
                result[lane] = list;
            }

            return result;
        }
    }
}
