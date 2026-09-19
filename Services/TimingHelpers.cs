using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        /// <summary>
        /// Sets or clears RunRemarks.Breakout (and Run.BreakoutDeltaNs) on a single lane
        /// based on its own finish ET vs its Index (dial-in), independent of the other
        /// lane's status - this also applies to a solo/bye run, whose Breakout remark is
        /// still shown even though the win is already decided by the bye-run rule.
        /// Only applies when the pair is running Eliminations/QeCombo and the category's
        /// ElimMode allows breakout (Breakout or BreakoutDiffClass); NoBreakout categories
        /// never get a Breakout remark. A missing/blank/zero Index parses to 0 seconds
        /// (see ParseHandicapSeconds), so it can never exceed a real ET and breakout
        /// simply never triggers for that lane.
        /// </summary>
        public static void EvaluateBreakoutRemark(Run? run, RunMode? mode, Category? category, long finishEtNs)
        {
            EvaluateBreakoutRemark(run, mode, category, finishEtNs, out _, out _);
        }

        /// <summary>
        /// Overload that also reports whether the Breakout remark's presence actually
        /// changed as a result of this call (<paramref name="changed"/>), and what its
        /// resulting state is (<paramref name="isBreakoutNow"/>) - used by callers that need
        /// to mirror the add/remove into external storage (e.g. persisting to run_remark).
        /// </summary>
        public static void EvaluateBreakoutRemark(Run? run, RunMode? mode, Category? category, long finishEtNs, out bool changed, out bool isBreakoutNow)
        {
            changed = false;
            isBreakoutNow = false;
            if (run == null || category == null) return;

            var m = mode ?? RunMode.Practice;
            bool eligible = (m == RunMode.Eliminations || m == RunMode.QeCombo)
                && category.ElimMode != ElimMode.NoBreakout
                && finishEtNs > 0;

            long indexNs = 0;
            bool isBreakout = false;
            if (eligible)
            {
                decimal indexSec = ParseHandicapSeconds(run.Entry?.HandicapIndex);
                indexNs = (long)Math.Round(indexSec * 1_000_000_000m);
                isBreakout = finishEtNs < indexNs;
            }

            isBreakoutNow = isBreakout;

            var remarksList = run.Remarks?.ToList() ?? new List<RunRemarks>();
            bool hasBreakout = remarksList.Contains(RunRemarks.Breakout);

            if (isBreakout)
            {
                run.BreakoutDeltaNs = finishEtNs - indexNs; // negative
                if (!hasBreakout) { remarksList.Add(RunRemarks.Breakout); run.Remarks = remarksList.ToArray(); changed = true; }
            }
            else
            {
                run.BreakoutDeltaNs = null;
                if (hasBreakout) { remarksList.Remove(RunRemarks.Breakout); run.Remarks = remarksList.ToArray(); changed = true; }
            }
        }

        /// <summary>
        /// Returns true if, given how much time has already elapsed since the waiting
        /// lane's own launch (relative to nowNs), it is now mathematically guaranteed
        /// that whenever the waiting lane actually finishes, its ET-vs-Index delta will
        /// be strictly greater than breakoutRun's delta - i.e. the waiting lane cannot
        /// tie or out-breakout breakoutRun, wherever its real finish time lands. Since
        /// ET only grows the longer the car is still on the track, "elapsed so far" is
        /// always a lower bound on the eventual final ET. Uses strict inequality so an
        /// exact eventual tie is never pre-empted by an early call.
        /// </summary>
        private static bool WaitingLaneCannotBeatBreakout(Run waitingRun, Run breakoutRun, long nowNs)
        {
            long triggerNs = waitingRun.ReactionTime?.TriggerTimestampNanoseconds ?? 0;
            long? breakoutDelta = breakoutRun.BreakoutDeltaNs;
            if (triggerNs <= 0 || !breakoutDelta.HasValue) return false;

            decimal indexSec = ParseHandicapSeconds(waitingRun.Entry?.HandicapIndex);
            long indexNs = (long)Math.Round(indexSec * 1_000_000_000m);
            long thresholdNs = indexNs + breakoutDelta.Value; // breakoutDelta negative -> earlier than indexNs
            long projectedElapsedNs = nowNs - triggerNs;

            return projectedElapsedNs > thresholdNs;
        }

        /// <summary>
        /// Decides Winner/Lose RunResult entries for Eliminations/QeCombo pairs.
        /// Winner/Lose are added to Run.Results alongside any existing FirstFinish/
        /// Indeterminate entries already there (Results is not mutually exclusive,
        /// except Winner and Lose can never both be present on the same run) - a
        /// run can be both FirstFinish and Lose (e.g. it crossed the stripe first
        /// but fouled the start), or both FirstFinish and Winner.
        /// Priority: (1) bye run (RunRemarks.NoVehicleStaged) always wins the staged
        /// lane; (2) if either lane has a deep-stage foul (RunRemarks.DsFoul), it
        /// decides outright - a single DsFoul lane loses and the other wins, but if
        /// BOTH lanes have DsFoul, both lose (nobody goes through); (3) otherwise, if
        /// either lane fouled (RunRemarks.Foul - a remark, not a result), the foul(s)
        /// decide, regardless of finish order; (3.5) otherwise, if either lane has a
        /// RunRemarks.Breakout (ET below its Index) and the category's ElimMode allows
        /// breakout to decide (Breakout, or BreakoutDiffClass with differing
        /// RaceEntry.Class values - same class/both null skips straight to (4)): a
        /// single breakout lane loses to the other lane outright (once the other
        /// lane's own finish is known, or once enough real time has passed that it's
        /// mathematically guaranteed the other lane cannot tie/out-breakout it - see
        /// WaitingLaneCannotBeatBreakout and the optional nowNs parameter); if BOTH
        /// lanes breakout, the least-negative (smallest undershoot) wins, unless they
        /// broke out by exactly the same amount, in which case no win is awarded to
        /// either lane; (4) otherwise, if neither lane fouled/broke out and exactly one
        /// lane has RunResult.FirstFinish, that lane wins conventionally - gated on
        /// Run.FinishEtNs being known for BOTH lanes (not on RunResult.FirstFinish
        /// itself, which may be assigned to a single lane as soon as it's established,
        /// before the other lane has necessarily finished, purely for early on-screen
        /// display - see MainWindowViewModel.MaybeCheckAndCompleteRun). Fully
        /// recomputes from current state each call so it self-corrects (e.g. a
        /// corrected-away Foul/DsFoul, or a FirstFinish assigned/swapped after this was
        /// last called).
        /// When Category.WorstFoul is true and at least one lane fouled (regular
        /// Foul, not DsFoul), no Winner/Lose is assigned until BOTH lanes' reaction
        /// times have been received (see HasReceivedReactionTime) - the "worst"
        /// comparison needs both values, so deciding early off a single foul could
        /// later have to be swapped once the second lane's (possibly worse) reaction
        /// time arrives. The decision is therefore only ever declared once, not
        /// flipped. DsFoul has no such waiting period - it's a direct treeserver
        /// report, not something requiring a reaction-time comparison.
        /// The optional nowNs parameter (Unix-epoch UTC nanoseconds) enables the early
        /// breakout decision described above; omit it (as all event-driven callers do)
        /// to only decide once a lane's own finish is genuinely known.
        /// </summary>
        public static void EvaluateWinnerLoseResult(Pair? pair, long? nowNs = null)
        {
            if (pair?.Runs == null || pair.Runs.Length < 2) return;
            var mode = pair.RunMode ?? RunMode.Practice;
            if (mode != RunMode.Eliminations && mode != RunMode.QeCombo) return;

            var category = pair.Category;
            if (category == null) return;

            ResolveWinnerLoseLanes(pair, out var left, out var right);
            if (left == null || right == null) return;

            bool leftNoVehicle = left.Remarks?.Contains(RunRemarks.NoVehicleStaged) ?? false;
            bool rightNoVehicle = right.Remarks?.Contains(RunRemarks.NoVehicleStaged) ?? false;

            // (1) Bye run: exactly one lane staged -> that lane always wins, foul or not.
            if (leftNoVehicle != rightNoVehicle)
            {
                var staged = leftNoVehicle ? right : left;
                var empty = leftNoVehicle ? left : right;
                SetResult(staged, RunResult.Winner, true);
                SetResult(staged, RunResult.Lose, false);
                SetResult(empty, RunResult.Winner, false);
                SetResult(empty, RunResult.Lose, false);
                return;
            }
            if (leftNoVehicle && rightNoVehicle) return; // nothing to decide

            // (2) Deep-stage foul: independent of, and takes priority over, reaction-time
            // fouls and finish order. A single DsFoul lane loses outright; if both lanes
            // have DsFoul, neither wins - both lose and nobody goes through.
            bool leftDsFoul = left.Remarks?.Contains(RunRemarks.DsFoul) ?? false;
            bool rightDsFoul = right.Remarks?.Contains(RunRemarks.DsFoul) ?? false;
            if (leftDsFoul || rightDsFoul)
            {
                if (leftDsFoul && rightDsFoul)
                {
                    // Nobody goes through: both lose, neither is a winner.
                    SetResult(left, RunResult.Lose, true);
                    SetResult(left, RunResult.Winner, false);
                    SetResult(right, RunResult.Lose, true);
                    SetResult(right, RunResult.Winner, false);
                    return;
                }

                var dsLoser = leftDsFoul ? left : right;
                var dsWinner = leftDsFoul ? right : left;
                SetResult(dsLoser, RunResult.Lose, true);
                SetResult(dsLoser, RunResult.Winner, false);
                SetResult(dsWinner, RunResult.Winner, true);
                SetResult(dsWinner, RunResult.Lose, false);
                return;
            }

            bool leftFoul = left.Remarks?.Contains(RunRemarks.Foul) ?? false;
            bool rightFoul = right.Remarks?.Contains(RunRemarks.Foul) ?? false;

            Run loser;
            if (leftFoul || rightFoul)
            {
                // When WorstFoul is true, the decision depends on comparing both lanes'
                // reaction times (the "worst"/most-negative one loses). Don't declare a
                // winner/loser off a single lane's foul before the other lane's reaction
                // time has actually been received/evaluated - doing so could later get
                // reversed (swapped) once the second reaction time comes in with a worse
                // delta. Wait for both before deciding at all, so it's only ever declared once.
                if (category.WorstFoul && !(HasReceivedReactionTime(left) && HasReceivedReactionTime(right)))
                {
                    SetResult(left, RunResult.Winner, false);
                    SetResult(left, RunResult.Lose, false);
                    SetResult(right, RunResult.Winner, false);
                    SetResult(right, RunResult.Lose, false);
                    return;
                }

                // (3) Foul-based decision always takes priority over finish order.
                if (leftFoul && rightFoul)
                {
                    if (category.WorstFoul)
                    {
                        long leftDelta = (left.ReactionTime?.ValueNs ?? 0) - (left.ReactionTime?.ExpectedReactionTimeNs ?? 0);
                        long rightDelta = (right.ReactionTime?.ValueNs ?? 0) - (right.ReactionTime?.ExpectedReactionTimeNs ?? 0);
                        loser = leftDelta <= rightDelta ? left : right; // more negative = worse
                    }
                    else
                    {
                        long leftTs = left.ReactionTime?.TriggerTimestampNanoseconds ?? long.MaxValue;
                        long rightTs = right.ReactionTime?.TriggerTimestampNanoseconds ?? long.MaxValue;
                        loser = leftTs <= rightTs ? left : right; // earliest = first negative RT received
                    }
                }
                else
                {
                    loser = leftFoul ? left : right;
                }
            }
            else
            {
                // (3.5) Breakout: only when the category allows it (and, under
                // BreakoutDiffClass, only when the two lanes' classes differ).
                bool leftBreakout = left.Remarks?.Contains(RunRemarks.Breakout) ?? false;
                bool rightBreakout = right.Remarks?.Contains(RunRemarks.Breakout) ?? false;

                bool classesMatch = string.Equals(
                    (left.Entry?.Class ?? string.Empty).Trim(),
                    (right.Entry?.Class ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase);
                bool skipBreakoutDecision = category.ElimMode == ElimMode.BreakoutDiffClass && classesMatch;

                if ((leftBreakout || rightBreakout) && !skipBreakoutDecision)
                {
                    if (leftBreakout && rightBreakout)
                    {
                        long leftDelta = left.BreakoutDeltaNs ?? long.MinValue;
                        long rightDelta = right.BreakoutDeltaNs ?? long.MinValue;

                        if (leftDelta == rightDelta)
                        {
                            // Same breakout amount, no fouls (we're in the non-Foul
                            // branch already) -> award no win.
                            SetResult(left, RunResult.Winner, false);
                            SetResult(left, RunResult.Lose, false);
                            SetResult(right, RunResult.Winner, false);
                            SetResult(right, RunResult.Lose, false);
                            return;
                        }

                        loser = leftDelta < rightDelta ? left : right; // more negative = worse breakout
                    }
                    else
                    {
                        // Exactly one lane is currently flagged breakout.
                        var breakoutRun = leftBreakout ? left : right;
                        var waitingRun = leftBreakout ? right : left;

                        if (waitingRun.FinishEtNs.HasValue)
                        {
                            // The other lane genuinely finished (without breaking out
                            // itself, since only one Breakout flag is set) -> it wins.
                            loser = breakoutRun;
                        }
                        else if (nowNs.HasValue && WaitingLaneCannotBeatBreakout(waitingRun, breakoutRun, nowNs.Value))
                        {
                            // Enough time has passed that the waiting lane can no
                            // longer tie or out-breakout breakoutRun - award it the
                            // win now. The literal finish event (when it arrives)
                            // will re-invoke this method and confirm/correct this.
                            loser = breakoutRun;
                        }
                        else
                        {
                            return; // still genuinely undecided - wait for the other lane's ET, or for enough time to pass
                        }
                    }
                }
                else
                {
                    // (4) No breakout decision applies (no breakout flagged, or the
                    // BreakoutDiffClass same-class exception): fall back to
                    // conventional first-finish win - but only once BOTH lanes have
                    // actually finished. Run.FinishEtNs (order-independent) is used
                    // here rather than RunResult.FirstFinish, because FirstFinish may
                    // now be assigned to a single lane as soon as it's established -
                    // i.e. before the other lane has necessarily finished, purely for
                    // early on-screen display - so it must NOT be used to gate the
                    // Winner/Lose decision.
                    bool bothFinished = left.FinishEtNs.HasValue && right.FinishEtNs.HasValue;
                    if (!bothFinished)
                    {
                        SetResult(left, RunResult.Winner, false);
                        SetResult(left, RunResult.Lose, false);
                        SetResult(right, RunResult.Winner, false);
                        SetResult(right, RunResult.Lose, false);
                        return;
                    }

                    bool leftFirst = left.Results?.Contains(RunResult.FirstFinish) ?? false;
                    bool rightFirst = right.Results?.Contains(RunResult.FirstFinish) ?? false;

                    if (leftFirst && !rightFirst)
                    {
                        loser = right;
                    }
                    else if (rightFirst && !leftFirst)
                    {
                        loser = left;
                    }
                    else
                    {
                        // Exact-timestamp tie (both Indeterminate) -> clear any stale decision.
                        SetResult(left, RunResult.Winner, false);
                        SetResult(left, RunResult.Lose, false);
                        SetResult(right, RunResult.Winner, false);
                        SetResult(right, RunResult.Lose, false);
                        return;
                    }
                }
            }

            var winner = ReferenceEquals(loser, left) ? right : left;

            // Add Winner/Lose alongside whatever else is already in Results (e.g.
            // FirstFinish) - only Winner and Lose are mutually exclusive with each other.
            SetResult(loser, RunResult.Lose, true);
            SetResult(loser, RunResult.Winner, false);
            SetResult(winner, RunResult.Winner, true);
            SetResult(winner, RunResult.Lose, false);
        }

        // A lane's reaction time is considered "received" once its raw trigger timestamp
        // has actually been recorded (ReactionTimeComputed sets ValueNs and
        // TriggerTimestampNanoseconds together), as opposed to a Run that simply hasn't
        // reached/reported its reaction time yet.
        private static bool HasReceivedReactionTime(Run run)
        {
            return run.ReactionTime != null && run.ReactionTime.TriggerTimestampNanoseconds > 0;
        }

        private static void SetResult(Run run, RunResult value, bool shouldHave)
        {
            var list = run.Results?.ToList() ?? new List<RunResult>();
            bool has = list.Contains(value);
            if (shouldHave && !has) { list.Add(value); run.Results = list.ToArray(); }
            else if (!shouldHave && has) { list.Remove(value); run.Results = list.ToArray(); }
        }

        private static void ResolveWinnerLoseLanes(Pair pair, out Run? left, out Run? right)
        {
            left = null; right = null;
            foreach (var r in pair.Runs)
            {
                if (r?.Entry == null) continue;
                if (r.Entry.Lane == 0) left = r;
                else if (r.Entry.Lane == 1) right = r;
            }
            if (left == null && pair.Runs.Length > 0) left = pair.Runs[0];
            if (right == null && pair.Runs.Length > 1) right = pair.Runs[1];
        }
    }
}
