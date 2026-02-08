using System;
using System.Globalization;
using PulsarUI.Models;

namespace PulsarUI.Services
{
    /// <summary>
    /// Helper methods for timing-related calculations.
    /// Populates Run.ReactionTime.ExpectedReactionTimeNs based on Pair, RunMode, Tree countdowns and handicap indexes.
    /// Assumptions:
    /// - RaceEntry.HandicapIndex is a string representing seconds (e.g. "00.20" -> 0.20 seconds).
    /// - TreeType.CountdownSpeed is in milliseconds.
    /// - If TreeType.CountdownType == 0 then x = CountdownSpeed; otherwise x = CountdownSpeed * 3.
    /// - ExpectedReactionTimeNs is stored as a long number of nanoseconds.
    /// </summary>
    public static class TimingHelpers
    {
        private const decimal SecondsToMilliseconds = 1000m;
        private const long MillisecondToNanoseconds = 1_000_000L;

        public static void PopulateExpectedReactionTimes(Pair? pair)
        {
            if (pair == null)
                return;

            // We expect two runs (lane 0 and lane 1); handle missing gracefully.
            Run? leftRun = null;
            Run? rightRun = null;
            foreach (var r in pair.Runs)
            {
                if (r.Entry == null) continue;
                if (r.Entry.Lane == 0) leftRun = r;
                else if (r.Entry.Lane == 1) rightRun = r;
            }

            // Fallback to array order if lane properties are not present
            if (leftRun == null && pair.Runs.Length > 0) leftRun = pair.Runs[0];
            if (rightRun == null && pair.Runs.Length > 1) rightRun = pair.Runs[1];

            // Compute x (ms) for each lane: x = CountdownSpeed or CountdownSpeed * 3
            decimal leftXms = GetXms(leftRun?.Entry);
            decimal rightXms = GetXms(rightRun?.Entry);

            // Parse handicap indexes as seconds
            decimal leftIndexSec = ParseHandicapSeconds(leftRun?.Entry?.HandicapIndex);
            decimal rightIndexSec = ParseHandicapSeconds(rightRun?.Entry?.HandicapIndex);

            // Convert index delta to milliseconds
            decimal indexDeltaMs = Math.Abs(leftIndexSec - rightIndexSec) * SecondsToMilliseconds;

            // Determine mode-specific behaviour
            var mode = pair.RunMode ?? RunMode.Practice;

            if (mode == RunMode.Practice || mode == RunMode.Qualifying)
            {
                // Expected reaction time is the maximum x (ms) between lanes, same for both runs
                decimal expectedMs = Math.Max(leftXms, rightXms);
                SetExpectedNs(leftRun, expectedMs);
                SetExpectedNs(rightRun, expectedMs);
                return;
            }

            // For Eliminations or QeCombo mode: can differ by lane
            if (mode == RunMode.Eliminations || mode == RunMode.QeCombo)
            {
                // If handicap indexes are equal (within small epsilon), expected per-lane = that lane's x
                if (Math.Abs(leftIndexSec - rightIndexSec) < 0.000001m)
                {
                    SetExpectedNs(leftRun, leftXms);
                    SetExpectedNs(rightRun, rightXms);
                    return;
                }

                // Handicap indexes differ: lane with highest index gets its x; other lane gets (indexDelta + x)
                if (leftIndexSec > rightIndexSec)
                {
                    SetExpectedNs(leftRun, leftXms);
                    SetExpectedNs(rightRun, rightXms + indexDeltaMs);
                }
                else
                {
                    SetExpectedNs(rightRun, rightXms);
                    SetExpectedNs(leftRun, leftXms + indexDeltaMs);
                }

                return;
            }

            // Default fallback: set per-lane x
            SetExpectedNs(leftRun, leftXms);
            SetExpectedNs(rightRun, rightXms);
        }

        private static decimal GetXms(RaceEntry? e)
        {
            if (e?.Tree == null) return 0m;
            var t = e.Tree;
            if (t.CountdownType == 0)
                return t.CountdownSpeed;
            return t.CountdownSpeed * 3m;
        }

        private static decimal ParseHandicapSeconds(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            // Try parse using invariant culture; expected format like "00.20"
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                return d;
            // Try allow colon or other separators? For now default 0
            return 0m;
        }

        private static void SetExpectedNs(Run? run, decimal expectedMs)
        {
            if (run == null) return;
            // Convert ms to ns and store as long
            decimal ns = expectedMs * MillisecondToNanoseconds;
            long nsLong;
            try
            {
                nsLong = (long)Math.Round(ns);
            }
            catch
            {
                nsLong = 0;
            }

            if (run.ReactionTime == null) run.ReactionTime = new ReactionTime();
            run.ReactionTime.ExpectedReactionTimeNs = nsLong;
        }
    }
}
