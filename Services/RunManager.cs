using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

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
        // - stageFallTimestampNs: nullable timestamp of a stage FALL event (direction == false). If null, treated as not observed.
        // - guardAEvents / guardBEvents: ordered (or unordered) lists of (timestampNs, direction) events for each guard. direction==true indicates rise (high).
        // - guardAEnabled / guardBEnabled: whether each guard is enabled in the race config.
        // Returns true and sets reactionTimeNanoseconds when a reaction time can be determined (either stage fall or guard rise overlap), otherwise false.
        public bool TryComputeReactionTime(
            string device,
            int input,
            long? stageFallTimestampNs,
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

            // If neither guard is enabled: reaction is strictly stage FALL.
            if (!guardAEnabled && !guardBEnabled)
            {
                if (stageFallTimestampNs.HasValue && stageFallTimestampNs.Value > runStart)
                {
                    reactionTimeNanoseconds = Math.Max(0, stageFallTimestampNs.Value - runStart);
                    detectionSource = "stage";
                    detectionTimestampNanoseconds = stageFallTimestampNs.Value;
                    return true;
                }
                return false;
            }

            // Single-guard enabled: whichever occurs first after runStart between stageRise and the earliest guard fall.
            if (guardAEnabled ^ guardBEnabled)
            {
                var guardEvents = guardAEnabled ? guardAEvents : guardBEvents;
                // With inverted polarity guards are detected on rising edges
                var earliestRise = FirstRiseAfter(guardEvents, runStart);

                long? chosenTimestamp = null;
                string chosenSource = "";
                if (stageFallTimestampNs.HasValue && stageFallTimestampNs.Value > runStart)
                {
                    chosenTimestamp = stageFallTimestampNs.Value;
                    chosenSource = "stage";
                }
                if (earliestRise.HasValue)
                {
                    if (!chosenTimestamp.HasValue || earliestRise.Value < chosenTimestamp.Value)
                    {
                        chosenTimestamp = earliestRise.Value;
                        chosenSource = guardAEnabled ? "guardA" : "guardB";
                    }
                }

                if (chosenTimestamp.HasValue)
                {
                    reactionTimeNanoseconds = Math.Max(0, chosenTimestamp.Value - runStart);
                    detectionSource = chosenSource;
                    detectionTimestampNanoseconds = chosenTimestamp.Value;
                    return true;
                }
                return false;
            }

            // Both guards enabled: need an interval where both guards are low simultaneously.
            // Compute HIGH intervals for each guard (guards are detected on rising edges)
            var aIntervals = ComputeHighIntervalsAfter(guardAEvents, runStart);
            var bIntervals = ComputeHighIntervalsAfter(guardBEvents, runStart);

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

            // Decide between guard-overlap detection and stage rise
            long? detectionTs = null;
            string detSource = "";
            if (earliestOverlapStart.HasValue)
            {
                detectionTs = earliestOverlapStart.Value;
                detSource = "guardsOverlap";
            }
            if (stageFallTimestampNs.HasValue && stageFallTimestampNs.Value > runStart)
            {
                if (!detectionTs.HasValue || stageFallTimestampNs.Value < detectionTs.Value)
                {
                    detectionTs = stageFallTimestampNs.Value;
                    detSource = "stage";
                }
            }

            if (detectionTs.HasValue)
            {
                reactionTimeNanoseconds = Math.Max(0, detectionTs.Value - runStart);
                detectionSource = detSource;
                detectionTimestampNanoseconds = detectionTs.Value;
                return true;
            }

            return false;
        }

        // Return the first rise (direction==true) strictly after runStart, or null if none
        private static long? FirstRiseAfter(IReadOnlyList<(long TimestampNanoseconds, bool Direction)>? events, long runStart)
        {
            if (events == null || events.Count == 0) return null;
            foreach (var ev in events.OrderBy(e => e.TimestampNanoseconds))
            {
                if (ev.TimestampNanoseconds <= runStart) continue;
                if (ev.Direction) // direction==true means rise -> high
                    return ev.TimestampNanoseconds;
            }
            return null;
        }

        // Compute list of HIGH intervals (start, end) for a guard, considering only events strictly after runStart.
        // An interval end of long.MaxValue indicates the guard remains high indefinitely.
        private static List<(long start, long end)> ComputeHighIntervalsAfter(IReadOnlyList<(long TimestampNanoseconds, bool Direction)>? events, long runStart)
        {
            var intervals = new List<(long start, long end)>();
            if (events == null || events.Count == 0) return intervals;

            var sorted = events.OrderBy(e => e.TimestampNanoseconds).ToList();

            // We assume guard is HIGH at runStart (do not infer high/low from pre-run events).
            long? curHighStart = runStart;

            // Iterate events strictly after runStart
            foreach (var ev in sorted)
            {
                if (ev.TimestampNanoseconds <= runStart) continue;
                if (!ev.Direction)
                {
                    // fall -> exit high if currently high
                    if (curHighStart.HasValue)
                    {
                        intervals.Add((curHighStart.Value, ev.TimestampNanoseconds));
                        curHighStart = null;
                    }
                }
                else
                {
                    // rise -> enter high if not already high
                    if (!curHighStart.HasValue)
                    {
                        curHighStart = ev.TimestampNanoseconds;
                    }
                }
            }

            if (curHighStart.HasValue)
            {
                intervals.Add((curHighStart.Value, long.MaxValue));
            }

            return intervals;
        }
    }
}
