using System;
using System.Text.Json;
using PulsarUI.Services;
using PulsarUI.Models;

class Program
{
    static void Main()
    {
        Console.WriteLine("SimulateIncremental: starting simulation");

        var mqtt = new MqttService();

        mqtt.ReactionTimeComputed += (lane, rtNs, runId, detectionSource, detectionTs) =>
        {
            var json = JsonSerializer.Serialize(new { reactionTimeNanoseconds = rtNs, runId = runId.ToString(), detectionSource });
            Console.WriteLine($"[PUBLISH] timingdata/{lane}/reactiontime -> {json}");
            var str = TimingLabelHelpers.FormatTimeFromNanosecondsNumeric(rtNs);
            Console.WriteLine($"[PUBLISH] timingdata/{lane}/reactiontime/string -> {str}");
        };
        mqtt.IncrementalTimeComputed += (lane, downtrackTs, incNs, speedMpsNullable, runId, dti) =>
        {
            // Safely handle nullable DownTrackInput and nullable speed
            var distanceMm = dti?.DistanceMm;
            var inputIndex = dti?.Id?.InputIndex;
            var json = JsonSerializer.Serialize(new { incrementalNanoseconds = incNs, speedMetersPerSecond = speedMpsNullable, runId = runId.ToString(), distanceMm = distanceMm, inputIndex = inputIndex });
            Console.WriteLine($"[PUBLISH] timingdata/{lane}/incrementaltime -> {json}");
            Console.WriteLine($"[PUBLISH] timingdata/{lane}/incrementaltime/string -> {TimingLabelHelpers.FormatTimeFromNanosecondsNumeric(incNs)}");
            var speedStr = speedMpsNullable.HasValue ? TimingLabelHelpers.FormatSpeed(speedMpsNullable.Value) : string.Empty;
            Console.WriteLine($"[PUBLISH] timingdata/{lane}/speedtrap/string -> {speedStr}");
        };

        var runManager = new RunManager();
        mqtt.AttachRunManager(runManager);

        // Use the repository inputmap.json so configured SpeedTraps are available
        var repoInputMap = "/home/david/OneDrive/Pulsar/Pulsar/PulsarUI/Config/inputmap.json";
        var ims = new InputMapService(repoInputMap);
        // Print configured SpeedTraps for visibility
        try
        {
            var trapsList = ims.GetSpeedTraps();
            Console.WriteLine("Configured SpeedTraps count=" + (trapsList?.Count ?? 0));
            if (trapsList != null)
            {
                foreach (var t in trapsList)
                {
                    Console.WriteLine($"  SpeedTrap: Start={t.StartMm} End={t.EndMm} (raw Id.Start={t.Id?.Start} Id.End={t.Id?.End})");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to enumerate traps: " + ex.Message);
        }
        mqtt.AttachInputMapService(ims);

        // Use the exact timestamps you provided in the message sequence so results match your test
        var runStartIso = "2026-02-09T19:30:00.000000000Z";
        var detectionIso = "2026-02-09T19:30:05.500000000Z"; // stage fall / detection
        var aIso = "2026-02-09T19:30:05.500000000Z"; // 15mm (input 6)
        var bIso = "2026-02-09T19:30:06.500000000Z"; // 18mm (input 7)
        var cIso = "2026-02-09T19:30:07.500000000Z"; // 80mm (input 8)
        var dIso = "2026-02-09T19:30:08.500000000Z"; // 100mm (device 192.168.20.22 input 1)

        long IsoToNs(string iso)
        {
            if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
                return (long)(dt - DateTime.UnixEpoch).TotalMilliseconds * 1_000_000L;
            return 0L;
        }

        var started = runManager.TryStartRun("sim-device", 1, IsoToNs(runStartIso), out var runId, forceRestart: true);
        Console.WriteLine($"RunManager.TryStartRun -> started={started} runId={runId}");

        var detectionTsNs = IsoToNs(detectionIso);
        // Emulate the MqttService internal behaviour by setting the left lane buffer DetectionTimestampNs and LastPublishedRunId
        // so that DispatchIncomingMessage will compute and publish incrementals for this run.
        try
        {
            var laneBuffersField = typeof(MqttService).GetField("_laneBuffers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (laneBuffersField != null)
            {
                var lb = laneBuffersField.GetValue(mqtt) as System.Collections.IDictionary;
                if (lb != null)
                {
                    var laneKey = "left";
                    if (!lb.Contains(laneKey))
                    {
                        var laneBufferType = typeof(MqttService).GetNestedType("LaneBuffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var newBuf = Activator.CreateInstance(laneBufferType!);
                        lb.Add(laneKey, newBuf);
                    }
                    var bufObj = lb[laneKey];
                    var detProp = bufObj.GetType().GetProperty("DetectionTimestampNs");
                    var lastPubProp = bufObj.GetType().GetProperty("LastPublishedRunId");
                    if (detProp != null) detProp.SetValue(bufObj, detectionTsNs);
                    if (lastPubProp != null) lastPubProp.SetValue(bufObj, runId);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Reflection set lane buffer failed: " + ex.Message);
        }

        string mkPayload(string sender, int input, int direction, string iso)
            => JsonSerializer.Serialize(new { sender = sender, input = input, direction = direction, timestamp = iso });

        // Dispatch the exact sequence you published
        var payloadRunStart = mkPayload("192.168.20.25", 1, 0, runStartIso);
        Console.WriteLine("\nDispatching RunStart: " + payloadRunStart);
        mqtt.DispatchIncomingMessage("inputs/timestamps", payloadRunStart).Wait();

        var payloadStage = mkPayload("192.168.20.21", 3, 0, detectionIso);
        Console.WriteLine("\nDispatching Stage: " + payloadStage);
        mqtt.DispatchIncomingMessage("inputs/timestamps", payloadStage).Wait();

        var payload15 = mkPayload("192.168.20.21", 6, 1, aIso);
        Console.WriteLine("\nDispatching 15mm: " + payload15);
        mqtt.DispatchIncomingMessage("inputs/timestamps", payload15).Wait();

        var payload18 = mkPayload("192.168.20.21", 7, 1, bIso);
        Console.WriteLine("\nDispatching 18mm: " + payload18);
        mqtt.DispatchIncomingMessage("inputs/timestamps", payload18).Wait();

        var payload80 = mkPayload("192.168.20.21", 8, 1, cIso);
        Console.WriteLine("\nDispatching 80mm: " + payload80);
        mqtt.DispatchIncomingMessage("inputs/timestamps", payload80).Wait();

        var payload100 = mkPayload("192.168.20.22", 1, 1, dIso);
        Console.WriteLine("\nDispatching 100mm: " + payload100);
        mqtt.DispatchIncomingMessage("inputs/timestamps", payload100).Wait();

        // Compute trap-based speed locally for the 15->18 trap so we can show the JSON payload that should be published when both sides are present.
        try
        {
            var aNs = IsoToNs(aIso);
            var bNs = IsoToNs(bIso);
            var cNs = IsoToNs(cIso);
            var dNs = IsoToNs(dIso);
            var runIdStr = runId.ToString();
            var incA = Math.Max(0, aNs - detectionTsNs);
            var incB = Math.Max(0, bNs - detectionTsNs);

            // Find the speed trap (Start=15 End=18)
            var traps = ims.GetSpeedTraps();
            var trap = traps.FirstOrDefault(t => t.StartMm == 15 && t.EndMm == 18);
            decimal? speedForEnd = null;
            if (trap != null)
            {
                var deltaMm = trap.EndMm - trap.StartMm;
                var deltaNs = Math.Max(0, bNs - aNs);
                if (deltaMm > 0 && deltaNs > 0)
                {
                    var distanceMeters = deltaMm / 1000m;
                    var timeSeconds = deltaNs / 1_000_000_000m;
                    speedForEnd = distanceMeters / timeSeconds; // m/s
                }
            }

            var jsonA = JsonSerializer.Serialize(new { incrementalNanoseconds = incA, speedMetersPerSecond = (decimal?)null, runId = runIdStr, distanceMm = 15, inputIndex = 6 });
            var jsonB = JsonSerializer.Serialize(new { incrementalNanoseconds = incB, speedMetersPerSecond = speedForEnd, runId = runIdStr, distanceMm = 18, inputIndex = 7 });
            // Also compute expected for 80->100 trap (80 start at cIso, 100 end at dIso)
            var trap80100 = traps.FirstOrDefault(t => t.StartMm == 80 && t.EndMm == 100);
            decimal? speed80100 = null;
            if (trap80100 != null)
            {
                var deltaMs = (dNs - cNs) / 1_000_000_000m;
                var deltaMeters = (trap80100.EndMm - trap80100.StartMm) / 1000m;
                if (deltaMs > 0) speed80100 = deltaMeters / deltaMs;
            }
            var jsonC = JsonSerializer.Serialize(new { incrementalNanoseconds = (Math.Max(0, cNs - IsoToNs(detectionIso))), speedMetersPerSecond = (decimal?)null, runId = runIdStr, distanceMm = 80, inputIndex = 8 });
            var jsonD = JsonSerializer.Serialize(new { incrementalNanoseconds = (Math.Max(0, dNs - IsoToNs(detectionIso))), speedMetersPerSecond = speed80100, runId = runIdStr, distanceMm = 100, inputIndex = 1 });

            Console.WriteLine("\nEXPECTED PUBLISH (start 15mm): timingdata/left/incrementaltime -> " + jsonA);
            Console.WriteLine("EXPECTED PUBLISH (start 15mm) string -> " + TimingLabelHelpers.FormatTimeFromNanosecondsNumeric(incA));
            Console.WriteLine("EXPECTED PUBLISH (start 15mm) speed string -> " + string.Empty);

            Console.WriteLine("\nEXPECTED PUBLISH (end 18mm): timingdata/left/incrementaltime -> " + jsonB);
            Console.WriteLine("EXPECTED PUBLISH (end 18mm) string -> " + TimingLabelHelpers.FormatTimeFromNanosecondsNumeric(incB));
            Console.WriteLine("EXPECTED PUBLISH (end 18mm) speed string -> " + (speedForEnd.HasValue ? TimingLabelHelpers.FormatSpeed(speedForEnd.Value) : string.Empty));

            Console.WriteLine("\nEXPECTED PUBLISH (start 80mm): timingdata/left/incrementaltime -> " + jsonC);
            Console.WriteLine("EXPECTED PUBLISH (end 100mm): timingdata/left/incrementaltime -> " + jsonD);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Trap compute failed: " + ex.Message);
        }

        Console.WriteLine("Simulation complete.");
    }
}
