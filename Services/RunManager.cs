using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace PulsarUI.Services
{
    public sealed class RunManager
    {
        private record RunInfo(long StartNanoseconds, DateTimeOffset Created, Guid RunId);

        private readonly ConcurrentDictionary<string, RunInfo> _runs = new();

        private static string Key(string device, int input) => $"{device}:{input}";

        public bool TryStartRun(string device, int input, long startNanoseconds, out Guid runId, bool forceRestart = false)
        {
            runId = Guid.Empty;
            var k = Key(device, input);
            if (forceRestart)
            {
                var info = new RunInfo(startNanoseconds, DateTimeOffset.UtcNow, Guid.NewGuid());
                _runs.AddOrUpdate(k, info, (_, _) => info);
                runId = info.RunId;
                return true;
            }

            var newInfo = new RunInfo(startNanoseconds, DateTimeOffset.UtcNow, Guid.NewGuid());
            var added = _runs.TryAdd(k, newInfo);
            runId = added ? newInfo.RunId : _runs[k].RunId;
            return added;
        }

        public bool TryGetElapsedNanoseconds(string device, int input, long currentNanoseconds, out long elapsedNanoseconds, out Guid runId)
        {
            elapsedNanoseconds = 0;
            runId = Guid.Empty;
            if (_runs.TryGetValue(Key(device, input), out var info))
            {
                var diff = currentNanoseconds - info.StartNanoseconds;
                elapsedNanoseconds = diff < 0 ? 0 : diff;
                runId = info.RunId;
                return true;
            }
            return false;
        }

        public bool TryStopRun(string device, int input)
        {
            return _runs.TryRemove(Key(device, input), out _);
        }

        public void Clear() => _runs.Clear();

        // Public API: compute reaction time for an active run identified by device+input.
        // Parameters:
        // - stageStartTimestampNs: nullable timestamp of a stage START (beam remade, direction == true). If null, treated as not observed.
        // - guardAEvents / guardBEvents: ordered (or unordered) lists of (timestampNs, direction) events for each guard. direction==true indicates rise (remade/high), direction==false indicates fall (blocked/low).
        // - guardAEnabled / guardBEnabled: whether each guard is enabled in the race config.
        // Returns true and sets reactionTimeNanoseconds when a reaction time can be determined (either stage start or guard blocked detection), otherwise false.
        public bool TryComputeReactionTime(
            string device,
            int input,
            long? stageStartTimestampNs,
            IReadOnlyList<(long TimestampNanoseconds, bool Direction)>? guardAEvents,
            IReadOnlyList<(long TimestampNanoseconds, bool Direction)>? guardBEvents,
            bool guardAEnabled,
            bool guardBEnabled,
            out long reactionTimeNanoseconds,
            out Guid runId,
            out string detectionSource,
            out long detectionTimestampNanoseconds)
        {
            reactionTimeNanoseconds = 0;
            runId = Guid.Empty;
            detectionTimestampNanoseconds = 0;
            detectionSource = "";
            if (!_runs.TryGetValue(Key(device, input), out var info)) return false;

            var runStart = info.StartNanoseconds;
            runId = info.RunId;

            // Normalize inputs to non-null lists
            guardAEvents ??= Array.Empty<(long, bool)>();
            guardBEvents ??= Array.Empty<(long, bool)>();

            // If neither guard is enabled: reaction is strictly stage START (remade).
            if (!guardAEnabled && !guardBEnabled)
            {
                if (stageStartTimestampNs.HasValue && stageStartTimestampNs.Value > runStart)
                {
                    reactionTimeNanoseconds = Math.Max(0, stageStartTimestampNs.Value - runStart);
                    detectionSource = "stage";
                    detectionTimestampNanoseconds = stageStartTimestampNs.Value;
                    try { File.AppendAllText("/tmp/pulsarui_debug.log", DateTimeOffset.UtcNow.ToString("o") + $" RunManager: device={device} input={input} runStart={runStart} detection=stage detectionTs={stageStartTimestampNs.Value} rt_ns={reactionTimeNanoseconds}\n"); } catch { }
                    return true;
                }
                return false;
            }

            // Single-guard enabled: whichever occurs first after runStart between stageRise and the earliest guard fall.
            if (guardAEnabled ^ guardBEnabled)
            {
                var guardEvents = guardAEnabled ? guardAEvents : guardBEvents;
                // Guards are considered blocked on FALL (direction==false). Find first fall after runStart.
                var earliestFall = FirstFallAfter(guardEvents, runStart);

                long? chosenTimestamp = null;
                string chosenSource = "";
                if (stageStartTimestampNs.HasValue && stageStartTimestampNs.Value > runStart)
                {
                    chosenTimestamp = stageStartTimestampNs.Value;
                    chosenSource = "stage";
                }
                if (earliestFall.HasValue)
                {
                    if (!chosenTimestamp.HasValue || earliestFall.Value < chosenTimestamp.Value)
                    {
                        chosenTimestamp = earliestFall.Value;
                        chosenSource = guardAEnabled ? "guardA" : "guardB";
                    }
                }

                if (chosenTimestamp.HasValue)
                {
                    reactionTimeNanoseconds = Math.Max(0, chosenTimestamp.Value - runStart);
                    detectionSource = chosenSource;
                    detectionTimestampNanoseconds = chosenTimestamp.Value;
                    try { File.AppendAllText("/tmp/pulsarui_debug.log", DateTimeOffset.UtcNow.ToString("o") + $" RunManager: device={device} input={input} runStart={runStart} detection={detectionSource} detectionTs={detectionTimestampNanoseconds} rt_ns={reactionTimeNanoseconds}\n"); } catch { }
                    return true;
                }
                return false;
            }

            // Both guards enabled: need an interval where both guards are BLOCKED (LOW) simultaneously.
            // Compute LOW intervals for each guard (blocked intervals), inferring pre-run state from events <= runStart.
            var aIntervals = ComputeLowIntervalsAfter(guardAEvents, runStart);
            var bIntervals = ComputeLowIntervalsAfter(guardBEvents, runStart);

            // Find the earliest overlap start among all interval pairs
            long? earliestOverlapStart = null;
            foreach (var a in aIntervals)
            {
                foreach (var b in bIntervals)
                {
                    var overlapStart = Math.Max(a.start, b.start);
                    var overlapEnd = Math.Min(a.end, b.end);
                    // allow zero-length overlap (both low at same timestamp) by using <=
                    if (overlapStart <= overlapEnd)
                    {
                        if (!earliestOverlapStart.HasValue || overlapStart < earliestOverlapStart.Value)
                            earliestOverlapStart = overlapStart;
                    }
                }
            }

            // Decide between guard-overlap detection and stage START (remade)
            long? detectionTs = null;
            string detSource = "";
            if (earliestOverlapStart.HasValue)
            {
                detectionTs = earliestOverlapStart.Value;
                detSource = "guardsOverlap";
            }
            if (stageStartTimestampNs.HasValue && stageStartTimestampNs.Value > runStart)
            {
                if (!detectionTs.HasValue || stageStartTimestampNs.Value < detectionTs.Value)
                {
                    detectionTs = stageStartTimestampNs.Value;
                    detSource = "stage";
                }
            }

            if (detectionTs.HasValue)
            {
                reactionTimeNanoseconds = Math.Max(0, detectionTs.Value - runStart);
                detectionSource = detSource;
                detectionTimestampNanoseconds = detectionTs.Value;
                try { File.AppendAllText("/tmp/pulsarui_debug.log", DateTimeOffset.UtcNow.ToString("o") + $" RunManager: device={device} input={input} runStart={runStart} detection={detectionSource} detectionTs={detectionTimestampNanoseconds} rt_ns={reactionTimeNanoseconds}\n"); } catch { }
                return true;
            }

            return false;
        }

        // Return the first fall (direction==false) strictly after runStart, or null if none
        private static long? FirstFallAfter(IReadOnlyList<(long TimestampNanoseconds, bool Direction)>? events, long runStart)
        {
            if (events == null || events.Count == 0) return null;
            foreach (var ev in events.OrderBy(e => e.TimestampNanoseconds))
            {
                if (ev.TimestampNanoseconds <= runStart) continue;
                if (!ev.Direction) // direction==false means fall -> blocked/low
                    return ev.TimestampNanoseconds;
            }
            return null;
        }

        // Compute list of LOW intervals (blocked) (start, end) for a guard, considering events before and after runStart
        // so the guard state at runStart can be inferred. An interval end of long.MaxValue indicates the guard remains low indefinitely.
        private static List<(long start, long end)> ComputeLowIntervalsAfter(IReadOnlyList<(long TimestampNanoseconds, bool Direction)>? events, long runStart)
        {
            var intervals = new List<(long start, long end)>();
            if (events == null || events.Count == 0) return intervals;

            var sorted = events.OrderBy(e => e.TimestampNanoseconds).ToList();

            // Infer the guard state at runStart from the last event <= runStart if present.
            // Direction==true => rise => HIGH/unblocked. Direction==false => fall => LOW/blocked.
            bool isHighAtRunStart = true; // default HIGH if unknown to avoid false immediate blocked overlap
            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                if (sorted[i].TimestampNanoseconds <= runStart)
                {
                    isHighAtRunStart = sorted[i].Direction;
                    break;
                }
            }

            long? curLowStart = null;
            if (!isHighAtRunStart)
            {
                // If guard was LOW at runStart, the low interval starts at runStart
                curLowStart = runStart;
            }

            // Iterate events strictly after runStart to build LOW intervals.
            foreach (var ev in sorted)
            {
                if (ev.TimestampNanoseconds <= runStart) continue;

                if (!ev.Direction)
                {
                    // fall -> enter LOW
                    if (!curLowStart.HasValue)
                    {
                        curLowStart = ev.TimestampNanoseconds;
                    }
                }
                else
                {
                    // rise -> exit LOW
                    if (curLowStart.HasValue)
                    {
                        intervals.Add((curLowStart.Value, ev.TimestampNanoseconds));
                        curLowStart = null;
                    }
                }
            }

            if (curLowStart.HasValue)
            {
                intervals.Add((curLowStart.Value, long.MaxValue));
            }

            return intervals;
        }
    }
}
