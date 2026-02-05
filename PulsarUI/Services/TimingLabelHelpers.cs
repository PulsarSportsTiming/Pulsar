using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PulsarUI.Models;

namespace PulsarUI.Services
{
    public static class TimingLabelHelpers
    {
        // Format a downtrack distance into the ET label according to selected distance unit.
        // Returns the ET-formatted label (e.g. "60' ET", "1/8 mi ET", "402.336m ET").
        // Supported distanceUnit values (case-insensitive): "m", "meters", "mi", "miles", "ft", "feet".
        public static string FormatDistanceLabel(int distanceMm, string distanceUnit)
        {
            if (distanceMm < 0) distanceMm = 0;
            var unit = (distanceUnit ?? "m").Trim().ToLowerInvariant();

            if (unit == "mi" || unit == "miles")
            {
                // exact fractional mile matches (strict mm equality)
                if (distanceMm == 201168) return "1/8 mi ET";
                if (distanceMm == 402336) return "1/4 mi ET";

                // default: show as feet with apostrophe
                var feet = (long)Math.Round(distanceMm / 304.8);
                return $"{feet}' ET";
            }

            if (unit == "ft" || unit == "feet")
            {
                var feet = (long)Math.Round(distanceMm / 304.8);
                return $"{feet}' ET";
            }

            // default: meters
            var meters = distanceMm / 1000.0;
            var s = meters.ToString("F3", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
            return $"{s}m ET";
        }

        // Format a speed-trap label that matches the distance representation used by FormatDistanceLabel,
        // but without the " ET" suffix and with the speed unit appended (e.g. "60' mph", "1/8 mi mph", "402.336 km/h").
        // If speedUnit is null/empty the distance representation is returned (without ET).
        public static string FormatSpeedTrapLabel(int distanceMm, string distanceUnit, string speedUnit)
        {
            var distLabel = FormatDistanceLabel(distanceMm, distanceUnit);
            if (distLabel.EndsWith(" ET", StringComparison.Ordinal))
                distLabel = distLabel.Substring(0, distLabel.Length - 3);

            if (string.IsNullOrWhiteSpace(speedUnit))
                return distLabel;

            return $"{distLabel} {speedUnit}";
        }

        // Generate per-lane timing label sequences from DownTrackInputs and SpeedTraps using strict mm equality for matching.
        // Returns a dictionary keyed by lane (InputLane.Left/InputLane.Right) with the ordered labels for that lane.
        // Each lane list contains: "Reaction Time", ET labels, any matching speed-trap labels immediately after the ET, then "Result".
        public static Dictionary<InputLane, List<string>> GenerateTimingLabels(
            List<DownTrackInput>? inputs,
            List<SpeedTrap>? traps,
            string distanceUnit,
            string speedUnit)
        {
            var result = new Dictionary<InputLane, List<string>>();
            result[InputLane.Left] = new List<string>();
            result[InputLane.Right] = new List<string>();

            if (inputs == null) return result;

            var trapEnds = new HashSet<int>(traps?.Select(t => t.EndMm) ?? Enumerable.Empty<int>());

            var inputsByLane = inputs
                .GroupBy(i => i.Lane)
                .ToDictionary(g => g.Key, g => g.OrderBy(i => i.DistanceMm).ToList());

            foreach (InputLane lane in Enum.GetValues(typeof(InputLane)))
            {
                var list = new List<string> { "Reaction Time" };
                var inputsForLane = inputsByLane.ContainsKey(lane) ? inputsByLane[lane] : new List<DownTrackInput>();

                var remainingTrapEnds = new HashSet<int>(trapEnds);

                foreach (var inp in inputsForLane)
                {
                    var etLabel = FormatDistanceLabel(inp.DistanceMm, distanceUnit);
                    list.Add(etLabel);

                    if (remainingTrapEnds.Contains(inp.DistanceMm))
                    {
                        var trapLabel = FormatSpeedTrapLabel(inp.DistanceMm, distanceUnit, speedUnit);
                        list.Add(trapLabel);
                        remainingTrapEnds.Remove(inp.DistanceMm);
                    }
                }

                list.Add("Result");
                result[lane] = list;
            }

            return result;
        }
    }
}
