using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Formatter;  // Added for MqttProtocolVersion
using System.Text.Json;
using System.Linq;
using PulsarUI.Models;
using System;
using System.IO;
using System.Collections.Generic;
using System.Buffers;  // Added for ReadOnlySequence

namespace PulsarUI.Services
{
    public class MqttService
    {
        // Event raised when a test message is received on the system/testmessage topic
        public event Action<string, string>? TestMessageReceived;
        public event Action<long>? RunStartReceived;
        // Event raised when a reaction time is computed by RunManager.
        // Parameters: lane ("left"/"right"), reactionTimeNanoseconds, runId, detectionSource, detectionTimestampNanoseconds
        public event Action<string, long, Guid, string, long>? ReactionTimeComputed;
        // Event raised when an incremental time (downtrack) is computed.
        // Parameters: lane ("left"/"right"), downtrackTimestampNanoseconds, incrementalNanoseconds, speedMetersPerSecond (nullable), runId, DownTrackInput
        public event Action<string, long, long, decimal?, Guid, DownTrackInput>? IncrementalTimeComputed;
        // Event raised when a lane's NoVehicle flag is updated via startline/runstart payload
        // Parameters: lane ("left"/"right"), noVehicle (bool)
        public event Action<string, bool>? LaneNoVehicleChanged;
        // Event raised when a lane's DS foul flag is updated via startline/runstart payload
        // Parameters: lane ("left"/"right"), dsFoul (bool)
        public event Action<string, bool>? LaneDsFoulChanged;
        // Event raised unconditionally whenever a timestamp message resolves to a configured InputRole
        // (excluding InputRole.Unknown). Used to drive the live System Status panel.
        // Parameters: role, direction (true = rising edge, false = falling edge)
        public event Action<InputRole, bool>? InputRoleStatusChanged;
        // Event raised unconditionally whenever a timestamp message resolves to a configured DownTrackInput.
        // Used to drive the live System Status panel.
        // Parameters: DownTrackInput, direction (true = rising edge, false = falling edge)
        public event Action<DownTrackInput, bool>? DownTrackInputStatusChanged;

        // Simple diagnostic log helper (append-only)
        private static void AppendLog(string msg)
        {
            try
            {
                File.AppendAllText("/tmp/pulsar_mqtt.log", DateTime.Now.ToString("o") + " " + msg + "\n");
            }
            catch { }

            try { Console.WriteLine("MQTTLOG: " + msg); } catch { }
        }

        // Helper to subscribe to the configured topics with a small retry loop.
        private async Task SubscribeToTopicsAsync()
        {
            if (_mqttClient == null) throw new InvalidOperationException("MQTT client not attached");

            var maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (_mqttClient.IsConnected != true)
                {
                    AppendLog($"SubscribeToTopicsAsync: client not connected (attempt {attempt}/{maxAttempts}), waiting...");
                    // Try to connect once more before subscribing
                    try
                    {
                        if (_options != null)
                            await _mqttClient.ConnectAsync(_options, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        AppendLog("SubscribeToTopicsAsync: ConnectAsync during subscribe attempt failed: " + ex.Message);
                    }
                }

                if (_mqttClient.IsConnected != true)
                {
                    if (attempt == maxAttempts)
                    {
                        throw new InvalidOperationException("MQTT client not connected after subscribe attempts");
                    }
                    await Task.Delay(250);
                    continue;
                }

                var subscribeOptionsBuilder = _mqttFactory.CreateSubscribeOptionsBuilder();
                foreach (var topic in _subscribeTopics)
                {
                    subscribeOptionsBuilder.WithTopicFilter(f => f.WithTopic(topic).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce));
                }

                var subscribeOptions = subscribeOptionsBuilder.Build();
                var subscribeResult = await _mqttClient.SubscribeAsync(subscribeOptions, CancellationToken.None);

                AppendLog($"SubscribeToTopicsAsync: subscribe attempted (attempt {attempt})");

                try
                {
                    if (subscribeResult?.Items != null)
                    {
                        foreach (var item in subscribeResult.Items)
                        {
                            AppendLog($"Subscribe result: TopicFilter={item.TopicFilter} ReasonCode={item.ResultCode}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppendLog("SubscribeToTopicsAsync: failed to enumerate subscribeResult items: " + ex.Message);
                }

                // If we reach here without throwing, assume subscribe succeeded
                return;
            }
        }

        private const string UnknownPair = "unknownpair";

        private IMqttClient? _mqttClient;
        private MqttClientOptions? _options;
        private readonly MqttClientFactory _mqttFactory;
        private readonly SemaphoreSlim _sync = new SemaphoreSlim(1, 1);
        private CancellationTokenSource? _reconnectCts;
        private Task? _reconnectTask;

        private readonly Dictionary<string, Func<string, string, Task>> _topicHandlers = new(StringComparer.OrdinalIgnoreCase);
        // Topics the service subscribes to (centralized for reuse when reconnecting)
        private readonly string[] _subscribeTopics = new[]
        {
            "system/testmessage",
            "inputs/status",
            "inputs/timestamps",
            "startline/runstart",
            "lanestart/#",
            "apibridge/competitors",
            "apibridge/bumpspot",
            "apibridge/categories",
            "apibridge/classes"
        };
        private InputMapService? _inputMapService;
        private RunManager? _runManager;
        private InputIdentifier? _runStartIdentifier;
        private InputIdentifier? _leftStageId;
        private InputIdentifier? _leftGuardAId;
        private InputIdentifier? _leftGuardBId;
        private InputIdentifier? _rightStageId;
        private InputIdentifier? _rightGuardAId;
        private InputIdentifier? _rightGuardBId;
        
            private class LaneBuffer
        {
            public List<(long ts, bool dir)> GuardA { get; } = new();
            public List<(long ts, bool dir)> GuardB { get; } = new();
            // Record all stage events (timestamp, direction) so we can find the first remade (direction==true)
            public List<(long ts, bool dir)> StageEvents { get; } = new();
            // When true, we are recording timestamps for a run until a lanestart source message arrives
            public bool RecordingForLanestart { get; set; } = false;
            public Guid LastPublishedRunId { get; set; } = Guid.Empty;
            // Store the detection timestamp (the raw event timestamp used to compute reaction time)
            public long? DetectionTimestampNs { get; set; }
            // Store the last downtrack event for speed delta computations (per-run)
            public int? LastDowntrackDistanceMm { get; set; }
            public long? LastDowntrackTimestampNs { get; set; }
            // Map trap key (startMm) -> timestamp Ns seen for this run
            public Dictionary<int,long> TrapStartTimestamps { get; } = new();
            public Dictionary<int,long> TrapEndTimestamps { get; } = new();
            // Track which downtrack inputs (device:inputIndex) have already reported a timestamp for the current run.
            // This lets us ignore repeated timestamps from the same physical input until the next run.
            public HashSet<string> SeenDowntrackInputs { get; } = new(StringComparer.Ordinal);
            // If true, this lane currently has no vehicle present and should not record RT or downtrack data
            public bool NoVehicle { get; set; }
            // If true, this lane has a DS foul (from startline/runstart) and should be remarked
            public bool DsFoul { get; set; }
            // If true, lane is marked foul (from startline/runstart) — tracked for completeness
            public bool Foul { get; set; }
        }

        private readonly Dictionary<string, LaneBuffer> _laneBuffers = new(); // key "left" or "right"

        // Track the device/input that started the current run (if known)
        private string? _currentRunDevice;
        private int? _currentRunInput;

        private const int ReactionTimeThresholdMs = 100; // 100 ms threshold for reaction time reporting

        // Helper: tolerant lookup to find a DownTrackInput from the configured InputMap
        private DownTrackInput? FindDownTrackInput(string device, int inputIndex)
        {
            // Delegate to InputMapService's TryFindDownTrackInput to reuse centralized logic and diagnostics
            try
            {
                if (_inputMapService == null)
                {
                    AppendLog("FindDownTrackInput: no InputMapService available");
                    return null;
                }
                return _inputMapService.TryFindDownTrackInput(device, inputIndex);
            }
            catch (Exception ex)
            {
                AppendLog("FindDownTrackInput error: " + ex.Message);
                return null;
            }
        }

        private static string NormalizeDevice(string? device)
        {
            if (string.IsNullOrWhiteSpace(device)) return string.Empty;
            var s = device.Trim();
            if (s.Length >= 2 && ((s.StartsWith("\"") && s.EndsWith("\"")) || (s.StartsWith("'") && s.EndsWith("'"))))
                s = s.Substring(1, s.Length - 2);
            return s;
        }

        private static string StripPort(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.StartsWith("[") && s.Contains("]"))
            {
                var close = s.IndexOf(']');
                var colonAfter = s.IndexOf(':', close);
                if (colonAfter > close) return s.Substring(0, colonAfter);
                return s;
            }
            var colonCount = s.Count(c => c == ':');
            if (colonCount == 1)
            {
                var idx = s.LastIndexOf(':');
                if (idx > 0) return s.Substring(0, idx);
            }
            return s;
        }

        // Helper to normalize lane key
        private static string LaneKey(InputRole role)
        {
            return role.ToString().StartsWith("Left", StringComparison.OrdinalIgnoreCase) ? "left" : "right";
        }

        // Ensure a buffer exists for lane
        private LaneBuffer GetBufferForLane(string lane)
        {
            lock (_laneBuffers)
            {
                if (!_laneBuffers.TryGetValue(lane, out var buf))
                {
                    buf = new LaneBuffer();
                    _laneBuffers[lane] = buf;
                }
                return buf;
            }
        }

        // Publishes reaction time JSON to timingdata/{lane}/reactiontime with detection source
        private Task PublishReactionTimeAsync(string lane, long reactionTimeNs, Guid runId, string detectionSource)
        {
            // Include a preformatted reaction time string so consumers can rely on the JSON payload only
            var payload = JsonSerializer.Serialize(new
            {
                reactionTimeNanoseconds = reactionTimeNs,
                reactionTimeString = TimingLabelHelpers.FormatTimeFromNanosecondsNumeric(reactionTimeNs),
                runId = runId.ToString(),
                detectionSource
            });
            var topic = $"timingdata/{lane}/reactiontime";
            return PublishMqtt(topic, payload);
        }

        // Minimal handler implementations. Expand these to perform real processing and integration with the rest of the app.
        private Task HandleInputsStatusAsync(string payload)
        {
            AppendLog("HandleInputsStatusAsync invoked");
            if (string.IsNullOrWhiteSpace(payload)) return Task.CompletedTask;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                // TODO: process doc.RootElement
            }
            catch (Exception ex)
            {
                AppendLog("inputs/status parse error: " + ex.Message);
            }
            return Task.CompletedTask;
        }

        private Task HandleInputTimestampsAsync(string payload)
        {
            AppendLog("HandleInputTimestampsAsync invoked");

            if (string.IsNullOrWhiteSpace(payload)) return Task.CompletedTask;
            
            var timestampModel = new Timestamp();

            try
            {
                // Parse the payload and map fields case-insensitively to the Timestamp model
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    AppendLog("inputs/timestamps: payload is not a JSON object");
                    return Task.CompletedTask;
                }

                // Helper: find a property by name (case-insensitive), return JsonElement if found
                JsonElement? FindProp(params string[] names)
                {
                    foreach (var prop in root.EnumerateObject())
                    {
                        foreach (var n in names)
                        {
                            if (string.Equals(prop.Name, n, StringComparison.OrdinalIgnoreCase))
                                return prop.Value;
                        }
                    }
                    return null;
                }

                // Extract fields (try common name variants)
                string? senderStr = null;
                if (FindProp("sender", "senderIp", "sender_ip") is JsonElement sEl && sEl.ValueKind != JsonValueKind.Null)
                    senderStr = sEl.ValueKind == JsonValueKind.String ? sEl.GetString() : sEl.ToString();

                int? inputVal = null;
                if (FindProp("input", "inputIndex") is JsonElement inEl && inEl.ValueKind != JsonValueKind.Null)
                {
                    if (inEl.ValueKind == JsonValueKind.Number && inEl.TryGetInt32(out var ival)) inputVal = ival;
                    else if (inEl.ValueKind == JsonValueKind.String && int.TryParse(inEl.GetString(), out var p)) inputVal = p;
                }

                bool? dirVal = null;
                if (FindProp("direction", "dir") is JsonElement dEl && dEl.ValueKind != JsonValueKind.Null)
                {
                    if (dEl.ValueKind == JsonValueKind.True || dEl.ValueKind == JsonValueKind.False) dirVal = dEl.GetBoolean();
                    else if (dEl.ValueKind == JsonValueKind.Number && dEl.TryGetInt32(out var di)) dirVal = di != 0;
                    else if (dEl.ValueKind == JsonValueKind.String && bool.TryParse(dEl.GetString(), out var db)) dirVal = db;
                }

                string? timestampIso = null;
                if (FindProp("timestamp", "time", "ts") is JsonElement tEl && tEl.ValueKind != JsonValueKind.Null)
                    timestampIso = tEl.ValueKind == JsonValueKind.String ? tEl.GetString() : tEl.ToString();

                // Build the model and convert the ISO timestamp to internal nanoseconds
                
                if (!string.IsNullOrEmpty(senderStr)) timestampModel.SenderIp = senderStr!;
                if (inputVal.HasValue) timestampModel.Input = inputVal.Value;
                if (dirVal.HasValue) timestampModel.Direction = dirVal.Value;

                if (!string.IsNullOrEmpty(timestampIso))
                {
                    try
                    {
                        timestampModel.SetFromIso(timestampIso!);
                    }
                    catch (Exception ex)
                    {
                        AppendLog("inputs/timestamps: SetFromIso failed: " + ex.Message);
                    }
                }

                // Use timestamp (store, index, raise events, etc.). Log a minimal summary for diagnostics.
                AppendLog($"Parsed timestamp sender={timestampModel.SenderIp} input={timestampModel.Input} ts_ns={timestampModel.TimestampNanoseconds}");
            }
            catch (Exception ex)
            {
                AppendLog("input/timestamps parse error: " + ex.Message);
            }

            // Attempt to lookup a configured downtrack input to include distance and lane in the test payload
            string extra = string.Empty;
            DownTrackInput? dti = null;
            try
            {
                if (_inputMapService != null)
                {
                    dti = _inputMapService.TryFindDownTrackInput(timestampModel.SenderIp, timestampModel.Input);
                }
                if (dti != null)
                {
                    extra = $" DistanceMm:{dti.DistanceMm} Lane:{(int)dti.Lane}";
                    AppendLog($"FindDownTrackInput: matched device='{timestampModel.SenderIp}' input={timestampModel.Input} -> distance={dti.DistanceMm} lane={(int)dti.Lane}");
                }
                else
                {
                    AppendLog($"FindDownTrackInput: no downtrack entries for device='{timestampModel.SenderIp}' normalized='{NormalizeDevice(timestampModel.SenderIp)}'");
                }
            }
            catch (Exception ex)
            {
                AppendLog("FindDownTrackInput lookup failed: " + ex.Message);
            }

            // Raise the live status event unconditionally for any matched DownTrackInput, regardless of
            // run/lane-buffer state, so the System Status panel always reflects the current edge direction.
            if (dti != null)
            {
                try
                {
                    DownTrackInputStatusChanged?.Invoke(dti, timestampModel.Direction);
                }
                catch (Exception ex)
                {
                    AppendLog("DownTrackInputStatusChanged handler threw: " + ex.Message);
                }
            }

            // If this timestamp matches a configured DownTrackInput and we have a recorded detection timestamp for that lane,
            // compute an incremental time and publish it and raise an event for the UI.
            // NOTE: only use downtrack timestamps with direction == false (falling edge) per configuration.
            try
            {
                if (dti != null && !timestampModel.Direction)
                {
                    var lane = dti.Lane.ToString().ToLowerInvariant();
                    var buf = GetBufferForLane(lane);
                    // If lane is marked as having no vehicle, ignore any downtrack timestamps
                    if (buf.NoVehicle)
                    {
                        AppendLog($"Incremental: ignoring downtrack timestamp from {(dti.Id?.Device ?? string.Empty)}:{dti.Id.InputIndex} lane={lane} ts={timestampModel.TimestampNanoseconds} (NoVehicle=true)");
                        return Task.CompletedTask;
                    }
                    // If we haven't computed & published a reaction time for this lane/run yet, ignore downtrack timestamps.
                    // Do NOT record them in SeenDowntrackInputs; they should be ignored until after RT is computed.
                    if (!buf.DetectionTimestampNs.HasValue || buf.LastPublishedRunId == Guid.Empty)
                    {
                        AppendLog($"Incremental: ignoring downtrack timestamp from {(dti.Id?.Device ?? string.Empty)}:{dti.Id.InputIndex} lane={lane} ts={timestampModel.TimestampNanoseconds} (no detection timestamp yet)");
                        return Task.CompletedTask;
                    }

                    // Now that we have a detection timestamp (reaction time published), ignore repeated timestamps from the same downtrack input
                    // for this run — only the first timestamp per physical input is processed.
                    try
                    {
                        var inputKey = (dti.Id?.Device ?? string.Empty) + ":" + dti.Id.InputIndex;
                        lock (buf)
                        {
                            if (buf.SeenDowntrackInputs.Contains(inputKey))
                            {
                                AppendLog($"Incremental: ignoring repeated timestamp from {inputKey} lane={lane} ts={timestampModel.TimestampNanoseconds}");
                                return Task.CompletedTask;
                            }
                            else
                            {
                                buf.SeenDowntrackInputs.Add(inputKey);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendLog("Incremental: failed checking/recording seen downtrack input: " + ex.Message);
                    }

                    if (buf.DetectionTimestampNs.HasValue && buf.LastPublishedRunId != Guid.Empty)
                     {
                         var runId = buf.LastPublishedRunId;
                         var incNs = Math.Max(0, timestampModel.TimestampNanoseconds - buf.DetectionTimestampNs.Value);
                         // Compute speed (m/s) if possible — only using configured SpeedTraps (no absolute-from-start speeds)
                         decimal? speedMps = null;
                         try
                         {
                             if (dti != null)
                             {
                                // Check if this distance corresponds to a configured SpeedTrap start or end
                                var st = _inputMapService?.FindSpeedTrapByDistance(dti.DistanceMm);
                                if (st == null)
                                {
                                    // Fallback: scan list and try to find a trap with Start or End equal to distance
                                    try
                                    {
                                        var all = _inputMapService?.GetSpeedTraps();
                                        if (all != null)
                                        {
                                            st = all.FirstOrDefault(x => x != null && (x.StartMm == dti.DistanceMm || x.EndMm == dti.DistanceMm));
                                            AppendLog($"Incremental: FindSpeedTrapByDistance returned null; fallback scan found={(st != null)} for distance {dti.DistanceMm}");
                                            if (st != null) AppendLog($"Incremental: fallback matched SpeedTrap Start={st.StartMm} End={st.EndMm}");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        AppendLog("Incremental: fallback scan for traps failed: " + ex.Message);
                                    }
                                }

                                AppendLog($"Incremental: lane={lane} distance={dti.DistanceMm} ts={timestampModel.TimestampNanoseconds} runIdCandidate={buf.LastPublishedRunId}");

                                if (st != null)
                                {
                                    AppendLog($"Incremental: matched SpeedTrap Start={st.StartMm} End={st.EndMm}");

                                    // If this distance matches the Start, record the timestamp under Start[StartMm]
                                    if (st.StartMm == dti.DistanceMm)
                                    {
                                        buf.TrapStartTimestamps[st.StartMm] = timestampModel.TimestampNanoseconds;
                                        AppendLog($"Incremental: recorded TrapStartTimestamps[{st.StartMm}] = {timestampModel.TimestampNanoseconds}");
                                    }

                                    // If it matches the End, record under End[StartMm]
                                    if (st.EndMm == dti.DistanceMm)
                                    {
                                        buf.TrapEndTimestamps[st.StartMm] = timestampModel.TimestampNanoseconds;
                                        AppendLog($"Incremental: recorded TrapEndTimestamps[{st.StartMm}] = {timestampModel.TimestampNanoseconds}");
                                    }

                                    // If we have both timestamps for this trap for this run, compute speed from delta
                                    if (buf.TrapStartTimestamps.TryGetValue(st.StartMm, out var tStart) && buf.TrapEndTimestamps.TryGetValue(st.StartMm, out var tEnd))
                                    {
                                        AppendLog($"Incremental: have both trap timestamps for start={st.StartMm}: tStart={tStart} tEnd={tEnd}");
                                        var deltaDistanceMm = st.EndMm - st.StartMm;
                                        var deltaTimeNs = Math.Max(0, tEnd - tStart);
                                        AppendLog($"Incremental: deltaDistanceMm={deltaDistanceMm} deltaTimeNs={deltaTimeNs}");
                                        if (deltaDistanceMm > 0 && deltaTimeNs > 0)
                                        {
                                            var distanceMeters = deltaDistanceMm / 1000m;
                                            var timeSeconds = deltaTimeNs / 1_000_000_000m;
                                            speedMps = distanceMeters / timeSeconds;
                                            AppendLog($"Incremental: computed speed_mps={speedMps} (m/s) for trap {st.StartMm}->{st.EndMm}");

                                            // Clear stored trap timestamps for this start so we only compute once per pair
                                            try
                                            {
                                                buf.TrapStartTimestamps.Remove(st.StartMm);
                                                buf.TrapEndTimestamps.Remove(st.StartMm);
                                                AppendLog($"Incremental: cleared TrapStart/End timestamps for start={st.StartMm}");
                                            }
                                            catch (Exception ex)
                                            {
                                                AppendLog($"Incremental: failed to clear trap timestamps for start={st.StartMm}: {ex.Message}");
                                            }
                                        }
                                        else
                                        {
                                            AppendLog($"Incremental: missing trap side for start={st.StartMm}: hasStart={buf.TrapStartTimestamps.ContainsKey(st.StartMm)} hasEnd={buf.TrapEndTimestamps.ContainsKey(st.StartMm)} deltaDistanceMm={deltaDistanceMm} deltaTimeNs={deltaTimeNs}");
                                            speedMps = null;
                                        }
                                    }
                                    else
                                    {
                                        // We don't have both sides yet; leave speedMps null and wait for the other side
                                        AppendLog($"Incremental: waiting for both trap timestamps for start={st.StartMm}");
                                        speedMps = null;
                                    }
                                }
                                else
                                {
                                    // No SpeedTrap configured for this distance; do not compute/publish speed
                                    AppendLog($"Incremental: no SpeedTrap configured for distance {dti.DistanceMm}");
                                    try
                                    {
                                        var allTraps = _inputMapService?.GetSpeedTraps();
                                        AppendLog($"Incremental: configured SpeedTraps count={allTraps?.Count ?? 0}");
                                        if (allTraps != null)
                                        {
                                            foreach (var tt in allTraps)
                                            {
                                                AppendLog($"Incremental: trap raw Id.Start={tt.Id?.Start} Id.End={tt.Id?.End} StartMm={tt.StartMm} EndMm={tt.EndMm}");
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        AppendLog("Incremental: failed to enumerate SpeedTraps: " + ex.Message);
                                    }
                                    speedMps = null;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppendLog("Speed compute (trap-based) failed: " + ex.Message);
                            speedMps = null;
                        }

                        // Fallback disabled: only compute speeds using configured SpeedTraps
                        try
                        {
                            AppendLog("Incremental: fallback disabled; only computing speeds using configured SpeedTraps");
                        }
                        catch { }

                        // Always update trap markers only; last-downtrack fallback removed
                        try
                        {
                            // No-op: preserve trap timestamp collections; last-downtrack markers are intentionally not used
                        }
                        catch { }

                        // Publish incremental time JSON to timingdata/{lane}/incrementaltime (including speed if available)
                        try
                        {
                            // Include both machine-friendly numeric values and human-friendly preformatted strings
                            // so consumers can use the JSON payload only and avoid separate /string topics.
                            var incPayload = JsonSerializer.Serialize(new
                            {
                                incrementalNanoseconds = incNs,
                                incrementalString = TimingLabelHelpers.FormatTimeFromNanosecondsNumeric(incNs),
                                speedMetersPerSecond = speedMps,
                                speedString = speedMps.HasValue ? TimingLabelHelpers.FormatSpeed(speedMps.Value) : string.Empty,
                                runId = runId.ToString(),
                                distanceMm = dti.DistanceMm,
                                inputIndex = dti.Id.InputIndex
                            });
                            _ = PublishMqtt($"timingdata/{lane}/incrementaltime", incPayload);
                            AppendLog($"Published incremental for lane={lane} inc_ns={incNs} speed_mps={speedMps} runId={runId} distanceMm={dti.DistanceMm}");
                        }
                        catch (Exception ex)
                        {
                            AppendLog("Publish incremental failed: " + ex.Message);
                        }

                        // Raise an event so ViewModel can update the Run model and UI
                        try
                        {
                            IncrementalTimeComputed?.Invoke(lane, timestampModel.TimestampNanoseconds, incNs, speedMps, runId, dti);
                        }
                        catch (Exception ex)
                        {
                            AppendLog("IncrementalTimeComputed handler threw: " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("Compute/publish incremental failed: " + ex.Message);
            }

            try
            {
                if (_inputMapService != null)
                {
                    var id = new InputIdentifier { Device = timestampModel.SenderIp ?? string.Empty, InputIndex = timestampModel.Input };
                    if (_inputMapService.TryGetRole(id, out InputRole role) && role != InputRole.Unknown)
                    {
                        extra += $" Role:{role}";
                        AppendLog($"TryGetRole: matched id='{id}' -> role={role}");

                        // Raise the live status event unconditionally for any matched role, regardless of
                        // run state, so the System Status panel always reflects the current edge direction.
                        try
                        {
                            InputRoleStatusChanged?.Invoke(role, timestampModel.Direction);
                        }
                        catch (Exception ex)
                        {
                            AppendLog("InputRoleStatusChanged handler threw: " + ex.Message);
                        }

                        // Handle relevant roles for run start / stage / guards
                        try
                        {
                            var ts = timestampModel.TimestampNanoseconds;

                            // If this is a RunStartTrigger, register the run in RunManager and notify ViewModel
                            if (role == InputRole.RunStartTrigger)
                            {
                                try
                                {
                                    // Only treat the RunStartTrigger as a run START when the input direction is a RISE (Direction==true).
                                    // Some hardware emits both rise and fall for the same input; ignoring falls prevents later events
                                    // from overwriting the initial run-start timestamp.
                                    if (!timestampModel.Direction)
                                    {
                                        AppendLog($"RunStartTrigger ignored (direction=fall) for {timestampModel.SenderIp}:{timestampModel.Input} ts={ts}");
                                    }
                                    else
                                    {
                                        if (!string.IsNullOrEmpty(timestampModel.SenderIp))
                                        {
                                            if (_runManager != null)
                                            {
                                                if (_runManager.TryStartRun(timestampModel.SenderIp, timestampModel.Input, ts, out var newRunId, forceRestart: true))
                                                {
                                                    AppendLog($"RunManager: started run {newRunId} for {timestampModel.SenderIp}:{timestampModel.Input} ts={ts}");
                                                }
                                                else
                                                {
                                                    AppendLog($"RunManager: run already present for {timestampModel.SenderIp}:{timestampModel.Input}");
                                                }
                                            }
                                            else
                                            {
                                                AppendLog("RunStartTrigger received but no RunManager attached; skipping run registration");
                                            }

                                            // Remember current run source so later sensor events can be associated
                                            _currentRunDevice = timestampModel.SenderIp;
                                            _currentRunInput = timestampModel.Input;

                                            // Reset per-lane published markers so we allow publishing RT for the new run
                                            lock (_laneBuffers)
                                            {
                                                foreach (var b in _laneBuffers.Values) b.LastPublishedRunId = Guid.Empty;
                                            }

                                            // Also reset last-downtrack info so speed deltas start fresh for the new run
                                            lock (_laneBuffers)
                                            {
                                                foreach (var b in _laneBuffers.Values)
                                                {
                                                    b.LastDowntrackDistanceMm = null;
                                                    b.LastDowntrackTimestampNs = null;
                                                    b.TrapStartTimestamps.Clear();
                                                    b.TrapEndTimestamps.Clear();
                                                    b.SeenDowntrackInputs.Clear();
                                                }
                                            }

                                            // Begin recording stage/guard events for lanestart confirmation until a lanestart/source message is received
                                            lock (_laneBuffers)
                                            {
                                                foreach (var b in _laneBuffers.Values)
                                                {
                                                    b.RecordingForLanestart = true;
                                                    b.StageEvents.Clear();
                                                    b.GuardA.Clear();
                                                    b.GuardB.Clear();
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    AppendLog("RunManager TryStartRun failed: " + ex.Message);
                                }

                                // Notify subscribers only when this input indicates a run START (rise)
                                try
                                {
                                    if (timestampModel.Direction)
                                    {
                                        RunStartReceived?.Invoke(ts);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    AppendLog("RunStartReceived handler threw: " + ex.Message);
                                }
                            }

                            // Stage or guard sensors — buffer and attempt RT computation
                            switch (role)
                            {
                                case InputRole.LeftStage:
                                case InputRole.RightStage:
                                case InputRole.LeftGuardA:
                                case InputRole.LeftGuardB:
                                case InputRole.RightGuardA:
                                case InputRole.RightGuardB:
                                    {
                                        var lane = LaneKey(role);
                                        var buf = GetBufferForLane(lane);
                                        lock (buf)
                                        {
                                            // Guard events
                                            if (role == InputRole.LeftGuardA || role == InputRole.RightGuardA)
                                            {
                                                buf.GuardA.Add((ts, timestampModel.Direction));
                                            }
                                            else if (role == InputRole.LeftGuardB || role == InputRole.RightGuardB)
                                            {
                                                buf.GuardB.Add((ts, timestampModel.Direction));
                                            }

                                            // Stage event: record all stage (timestamp, direction) events so we can find the first remade (direction==true)
                                            if (role == InputRole.LeftStage || role == InputRole.RightStage)
                                            {
                                                buf.StageEvents.Add((ts, timestampModel.Direction));
                                            }
                                        }

                                        // Attempt reaction time computation if we have a current run
                                        try
                                        {
                                            if (!string.IsNullOrEmpty(_currentRunDevice) && _currentRunInput.HasValue)
                                            {
                                                // Decide guard-enabled flags by querying the InputMapService at runtime so config changes are respected
                                                bool guardAEnabled = false, guardBEnabled = false;
                                                try
                                                {
                                                    if (_inputMapService != null)
                                                    {
                                                        if (lane == "left")
                                                        {
                                                            _inputMapService.TryGetIdentifier(InputRole.LeftGuardA, out var gA);
                                                            _inputMapService.TryGetIdentifier(InputRole.LeftGuardB, out var gB);
                                                            guardAEnabled = (gA != null && gA.Enabled);
                                                            guardBEnabled = (gB != null && gB.Enabled);
                                                        }
                                                        else
                                                        {
                                                            _inputMapService.TryGetIdentifier(InputRole.RightGuardA, out var gA);
                                                            _inputMapService.TryGetIdentifier(InputRole.RightGuardB, out var gB);
                                                            guardAEnabled = (gA != null && gA.Enabled);
                                                            guardBEnabled = (gB != null && gB.Enabled);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        // Fallback to cached identifiers if InputMapService not available
                                                        guardAEnabled = (lane == "left") ? (_leftGuardAId != null && _leftGuardAId.Enabled) : (_rightGuardAId != null && _rightGuardAId.Enabled);
                                                        guardBEnabled = (lane == "left") ? (_leftGuardBId != null && _leftGuardBId.Enabled) : (_rightGuardBId != null && _rightGuardBId.Enabled);
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    AppendLog("Error querying InputMapService for guard enabled flags: " + ex.Message);
                                                    guardAEnabled = (lane == "left") ? (_leftGuardAId != null && _leftGuardAId.Enabled) : (_rightGuardAId != null && _rightGuardAId.Enabled);
                                                    guardBEnabled = (lane == "left") ? (_leftGuardBId != null && _leftGuardBId.Enabled) : (_rightGuardBId != null && _rightGuardBId.Enabled);
                                                }

                                                long rtNs;
                                                Guid runId;
                                                // Copy lists under lock to avoid concurrent modification
                                                 List<(long ts, bool dir)> aCopy, bCopy; long? sStart;
                                                 var b = GetBufferForLane(lane);
                                                 lock (b)
                                                 {
                                                     aCopy = b.GuardA.ToList();
                                                     bCopy = b.GuardB.ToList();
                                                     // Determine the first stage remade (direction==true) observed in the buffer
                                                     var firstRemade = b.StageEvents.Where(x => x.dir).OrderBy(x => x.ts).Select(x => x.ts).FirstOrDefault();
                                                     sStart = firstRemade == 0 ? (long?)null : firstRemade;
                                                 }

                                                  // Diagnostic: attempt to infer stored runStart for logging by asking RunManager for elapsed at this event timestamp
                                                  long runStartForLog = -1;
                                                  try
                                                  {
                                                      if (_runManager != null && _runManager.TryGetElapsedNanoseconds(_currentRunDevice, _currentRunInput.Value, ts, out var elapsedForLog, out var ridForLog))
                                                      {
                                                          runStartForLog = ts - elapsedForLog;
                                                      }
                                                  }
                                                  catch { }

                                                  AppendLog($"TryComputeReactionTime: lane={lane} eventTs={ts} inferredRunStart={runStartForLog} sStart={(sStart.HasValue? sStart.Value.ToString():"<null>")} guardAEvents={aCopy.Count} guardBEvents={bCopy.Count} guardAEnabled={guardAEnabled} guardBEnabled={guardBEnabled}");

                                                  if (_runManager != null && _runManager.TryComputeReactionTime(_currentRunDevice, _currentRunInput.Value, sStart, aCopy, bCopy, guardAEnabled, guardBEnabled, out rtNs, out runId, out var detectionSource, out var detectionTimestampNs))
                                                {
                                                    // Publish once per run per lane
                                                    var laneBuf = GetBufferForLane(lane);
                                                    // If NoVehicle is set for this lane, do not record or publish reaction times
                                                    if (laneBuf.NoVehicle)
                                                    {
                                                        AppendLog($"RT suppressed for lane={lane} because NoVehicle=true");
                                                    }
                                                    else
                                                    {
                                                            if (laneBuf.LastPublishedRunId != runId)
                                                            {
                                                                laneBuf.LastPublishedRunId = runId;
                                                                // Do NOT publish MQTT here. The ViewModel subscribes to ReactionTimeComputed,
                                                                // populates ExpectedReactionTimeNs and will publish timingdata/{lane}/reactiontime
                                                                // so the reactionTimeString matches the on-screen value.
                                                                AppendLog($"Computed reaction time for lane={lane} rt_ns={rtNs} runId={runId} source={detectionSource} (publish deferred to ViewModel)");
                                                                // Store detection timestamp so later down-track timestamps can compute incremental times
                                                                laneBuf.DetectionTimestampNs = detectionTimestampNs;
                                                                // Notify subscribers (e.g., ViewModel) so they can attach ReactionTime to the Run model
                                                                try
                                                                {
                                                                    ReactionTimeComputed?.Invoke(lane, rtNs, runId, detectionSource, detectionTimestampNs);
                                                                }
                                                                catch (Exception ex)
                                                                {
                                                                    AppendLog("ReactionTimeComputed handler threw: " + ex.Message);
                                                                }
                                                            }
                                                    }
                                                 }
                                                else if (_runManager == null)
                                                {
                                                    AppendLog("RunManager not attached; cannot compute reaction time");
                                                }

                                                break;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            AppendLog("Compute/publish RT failed: " + ex.Message);
                                        }

                                        break;
                                    }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppendLog("Timestamp role handler failed: " + ex.Message);
                        }
                    }
                    else
                    {
                        AppendLog($"TryGetRole: no role for id='{id}'");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("TryGetRole lookup failed: " + ex.Message);
            }
            
            // (RunStartTrigger handling moved into the main role handler above)

            //TestMessageReceived?.Invoke("Timestamp", payload + " Sender:" + timestampModel.SenderIp + " Input:" + timestampModel.Input + " TS(ns):" + timestampModel.TimestampNanoseconds + extra);
            return Task.CompletedTask;
        }

        private Task HandleApiCompetitorsAsync(string payload)
        {
            AppendLog("HandleApiCompetitorsAsync invoked");
            if (string.IsNullOrWhiteSpace(payload)) return Task.CompletedTask;
            try
            {
                // Example: attempt to deserialize to a dynamic structure or known model
                // var competitors = JsonSerializer.Deserialize<List<Competitor>>(payload);
            }
            catch (Exception ex)
            {
                AppendLog("apibridge/competitors parse error: " + ex.Message);
            }
            return Task.CompletedTask;
        }

        private Task HandleApiBumpSpotAsync(string payload)
        {
            AppendLog("HandleApiBumpSpotAsync invoked");
            if (string.IsNullOrWhiteSpace(payload)) return Task.CompletedTask;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                // TODO: process bump spot payload
            }
            catch (Exception ex)
            {
                AppendLog("apibridge/bumpspot parse error: " + ex.Message);
            }
            return Task.CompletedTask;
        }

        private Task HandleApiCategoriesAsync(string payload)
        {
            AppendLog("HandleApiCategoriesAsync invoked");
            if (string.IsNullOrWhiteSpace(payload)) return Task.CompletedTask;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                // TODO: process categories payload
            }
            catch (Exception ex)
            {
                AppendLog("apibridge/categories parse error: " + ex.Message);
            }
            return Task.CompletedTask;
        }
        
        private Task HandleApiClassesAsync(string payload)
        {
            AppendLog("HandleApiClassesAsync invoked");
            if (string.IsNullOrWhiteSpace(payload)) return Task.CompletedTask;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                // TODO: process classes payload
            }
            catch (Exception ex)
            {
                AppendLog("apibridge/classes parse error: " + ex.Message);
            }
            return Task.CompletedTask;
        }

        public MqttService()
        {
            _mqttFactory = new MqttClientFactory();
            RegisterTopicHandlers();
        }

        public void AttachRunManager(RunManager runManager) 
        { 
            _runManager = runManager ?? throw new ArgumentNullException(nameof(runManager)); 
        }

        public void AttachInputMapService(InputMapService inputMapService)
        {
            _inputMapService = inputMapService ?? throw new ArgumentNullException(nameof(inputMapService));
            try
            {
                _inputMapService.TryGetIdentifier(InputRole.RunStartTrigger, out _runStartIdentifier);
                _inputMapService.TryGetIdentifier(InputRole.LeftStage, out _leftStageId);
                _inputMapService.TryGetIdentifier(InputRole.LeftGuardA, out _leftGuardAId);
                _inputMapService.TryGetIdentifier(InputRole.LeftGuardB, out _leftGuardBId);
                _inputMapService.TryGetIdentifier(InputRole.RightStage, out _rightStageId);
                _inputMapService.TryGetIdentifier(InputRole.RightGuardA, out _rightGuardAId);
                _inputMapService.TryGetIdentifier(InputRole.RightGuardB, out _rightGuardBId);
            }
            catch (Exception ex) { AppendLog("AttachInputMapService cache identifiers failed: " + ex.Message); }
            
            // Emit configured SpeedTraps for diagnostics
            try
            {
                var traps = _inputMapService?.GetSpeedTraps();
                AppendLog($"AttachInputMapService: SpeedTraps count={traps?.Count ?? 0}");
                if (traps != null)
                {
                    foreach (var t in traps)
                    {
                        AppendLog($"SpeedTrap: Start={t.StartMm} End={t.EndMm}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("AttachInputMapService: failed to enumerate SpeedTraps: " + ex.Message);
            }
        }

        /// <summary>
        /// Attach a pre-configured MQTT client and options (from DI/composition root).
        /// Follows pattern from Client_Connection_Samples.cs Connect() method.
        /// </summary>
        public void AttachMqttClient(IMqttClient client, MqttClientOptions options)
        {
            _mqttClient = client ?? throw new ArgumentNullException(nameof(client));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            AppendLog("AttachMqttClient: MQTT client and options attached");

            // Setup message handler
            _mqttClient.ApplicationMessageReceivedAsync += HandleApplicationMessageAsync;

            // Connect and subscribe (fire-and-forget)
            _ = ConnectAndSubscribeAsync();
        }

        /// <summary>
        /// Create and attach an MQTT client for a given host/port.
        /// Follows pattern from Client_Connection_Samples.cs Connect() method.
        /// </summary>
        public void AttachMqttClientFromHost(string host, int port = 1883, string clientId = "PulsarUI")
        {
            AppendLog($"AttachMqttClientFromHost: creating client for {host}:{port}");

            try
            {
                // Create client using factory (like samples)
                _mqttClient = _mqttFactory.CreateMqttClient();

                // Build options using builder pattern (like samples)
                // Ensure a reasonably unique client id so multiple instances or reconnects don't
                // cause the broker to drop an existing session immediately.
                var uniqueClientId = clientId + "-" + Guid.NewGuid().ToString("N");

                _options = new MqttClientOptionsBuilder()
                    .WithTcpServer(host, port)
                    .WithClientId(uniqueClientId)
                    .WithCleanSession()
                        // Prefer MQTT v3.1.1 for broader broker compatibility; v5 is supported but some brokers
                        // may interoperate better when using v3.1.1. Change to V500 if you need v5 features.
                        .WithProtocolVersion(MqttProtocolVersion.V311)
                    .Build();

                AppendLog($"AttachMqttClientFromHost: client created for {host}:{port}");
                AppendLog($"AttachMqttClientFromHost: using clientId={uniqueClientId}");

                // Setup message and connection handlers for diagnostics and automatic resubscribe
                _mqttClient.ApplicationMessageReceivedAsync += HandleApplicationMessageAsync;
                _mqttClient.ConnectedAsync += async args => {
                    AppendLog("MQTT client Connected");
                    try
                    {
                        // Attempt to re-subscribe to topics when connected
                        await SubscribeToTopicsAsync();
                    }
                    catch (Exception ex)
                    {
                        AppendLog("ConnectedAsync resubscribe failed: " + ex.Message);
                    }
                };
                _mqttClient.DisconnectedAsync += args => {
                    AppendLog("MQTT client Disconnected: " + (args?.Exception?.Message ?? "no exception"));
                    return Task.CompletedTask;
                };

                // Connect and subscribe (fire-and-forget)
                _ = ConnectAndSubscribeAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"AttachMqttClientFromHost failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Connect to broker and subscribe to topics.
        /// Follows pattern from Client_Connection_Samples.cs Connect() method.
        /// </summary>
        private async Task ConnectAndSubscribeAsync()
        {
            if (_mqttClient == null || _options == null)
            {
                AppendLog("ConnectAndSubscribeAsync: client or options not initialized");
                return;
            }

            try
            {
                AppendLog("ConnectAndSubscribeAsync: attempting ConnectAsync...");
                
                // Connect (like samples)
                var response = await _mqttClient.ConnectAsync(_options, CancellationToken.None);
                
                AppendLog($"ConnectAndSubscribeAsync: connected with result {response.ResultCode}");

                try
                {
                    // Log a few diagnostic fields from the connect response if present
                    AppendLog($"Connect response: {response}");
                }
                catch { }

                // Give the client a short moment to settle before attempting subscriptions; some transports
                // may report connected immediately but still be finalizing the socket state.
                await Task.Delay(200);

                // Subscribe to topics (attempt with a small retry if the client temporarily disconnects)
                try
                {
                    await SubscribeToTopicsAsync();
                }
                catch (Exception ex)
                {
                    AppendLog("ConnectAndSubscribeAsync failed subscribing: " + ex.Message);
                }

                // Start reconnection monitor (like Reconnect_Using_Timer sample)
                StartReconnectionMonitor();
            }
            catch (Exception ex)
            {
                AppendLog($"ConnectAndSubscribeAsync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Reconnection logic following Reconnect_Using_Timer() sample pattern.
        /// </summary>
        private void StartReconnectionMonitor()
        {
            // Stop existing monitor if any
            _reconnectCts?.Cancel();
            _reconnectCts = new CancellationTokenSource();

            _reconnectTask = Task.Run(async () =>
            {
                while (!_reconnectCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), _reconnectCts.Token);

                        if (_mqttClient == null || _options == null)
                            continue;

                        // Check connection using TryPingAsync (like sample)
                        if (!await _mqttClient.TryPingAsync(_reconnectCts.Token))
                        {
                            AppendLog("Reconnection monitor: connection lost, reconnecting...");
                            await _mqttClient.ConnectAsync(_options, _reconnectCts.Token);
                            AppendLog("Reconnection monitor: reconnected successfully");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Reconnection monitor error: {ex.Message}");
                    }
                }
            }, _reconnectCts.Token);
        }

        /// <summary>
        /// Ensure connected before operations.
        /// </summary>
        private async Task EnsureConnectedAsync()
        {
            if (_mqttClient == null || _options == null)
            {
                AppendLog("EnsureConnectedAsync: creating default client for localhost:1883");
                AttachMqttClientFromHost("192.168.20.100", 1883);
            }

            if (_mqttClient?.IsConnected != true)
            {
                await _sync.WaitAsync();
                try
                {
                    if (_mqttClient?.IsConnected != true && _options != null)
                    {
                        AppendLog("EnsureConnectedAsync: connecting...");
                        await _mqttClient!.ConnectAsync(_options, CancellationToken.None);
                    }
                }
                finally
                {
                    _sync.Release();
                }
            }
        }

        /// <summary>
        /// Handle incoming MQTT messages.
        /// </summary>
        private Task HandleApplicationMessageAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                var topic = e.ApplicationMessage.Topic;
                
                // Convert ReadOnlySequence<byte> to string
                var payloadSequence = e.ApplicationMessage.Payload;
                string payload;
                
                if (payloadSequence.IsSingleSegment)
                {
                    payload = System.Text.Encoding.UTF8.GetString(payloadSequence.FirstSpan);
                }
                else
                {
                    // Handle multi-segment payload
                    var buffer = ArrayPool<byte>.Shared.Rent((int)payloadSequence.Length);
                    try
                    {
                        payloadSequence.CopyTo(buffer);
                        payload = System.Text.Encoding.UTF8.GetString(buffer, 0, (int)payloadSequence.Length);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
                
                AppendLog($"Message received on topic '{topic}'");

                return DispatchIncomingMessage(topic, payload);
            }
            catch (Exception ex)
            {
                AppendLog($"HandleApplicationMessageAsync error: {ex.Message}");
                return Task.CompletedTask;
            }
        }

        private void RegisterTopicHandlers()
        {
            _topicHandlers["inputs/status"] = (topic, payload) => HandleInputsStatusAsync(payload);
            _topicHandlers["inputs/timestamps"] = (topic, payload) => HandleInputTimestampsAsync(payload);
            _topicHandlers["startline/runstart"] = (topic, payload) => HandleStartLineRunStartAsync(payload);
            _topicHandlers["lanestart/"] = (topic, payload) => HandleLaneStartSourceAsync(topic, payload);
            _topicHandlers["apibridge/competitors"] = (topic, payload) => HandleApiCompetitorsAsync(payload);
            _topicHandlers["apibridge/bumpspot"] = (topic, payload) => HandleApiBumpSpotAsync(payload);
            _topicHandlers["apibridge/categories"] = (topic, payload) => HandleApiCategoriesAsync(payload);
            _topicHandlers["apibridge/classes"] = (topic, payload) => HandleApiClassesAsync(payload);
            _topicHandlers["system/testmessage"] = (topic, payload) => HandleTestMessageAsync(payload);
        }

        public Task DispatchIncomingMessage(string topic, string payload)
        {
            try
            {
                if (string.IsNullOrEmpty(topic)) 
                    return Task.CompletedTask;

                // Exact match
                if (_topicHandlers.TryGetValue(topic, out var handler))
                    return handler(topic, payload);

                // Prefix match
                foreach (var kv in _topicHandlers)
                {
                    if (topic.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                        return kv.Value(topic, payload);
                }

                AppendLog($"DispatchIncomingMessage: no handler for topic '{topic}'");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                AppendLog($"DispatchIncomingMessage error: {ex.Message}");
                return Task.CompletedTask;
            }
        }

        private Task HandleTestMessageAsync(string payload)
        {
            AppendLog($"HandleTestMessageAsync: {payload}");
            TestMessageReceived?.Invoke("system/testmessage", payload);
            return Task.CompletedTask;
        }

        private Task HandleStartLineRunStartAsync(string payload)
        {
            AppendLog($"HandleStartLineRunStartAsync: {payload}");
            if (string.IsNullOrWhiteSpace(payload)) return Task.CompletedTask;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return Task.CompletedTask;

                // Lanes array expected
                if (root.TryGetProperty("Lanes", out var lanesEl) && lanesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var laneEl in lanesEl.EnumerateArray())
                    {
                        int laneIndex = -1;
                        bool noVehicle = false;
                        bool dsFoul = false;
                        bool foul = false;
                        try
                        {
                            if (laneEl.TryGetProperty("Lane", out var l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out var li)) laneIndex = li;
                            if (laneEl.TryGetProperty("NoVehicle", out var nv) && nv.ValueKind == JsonValueKind.True) noVehicle = true;
                            if (laneEl.TryGetProperty("DSFoul", out var ds) && ds.ValueKind == JsonValueKind.True) dsFoul = true;
                            if (laneEl.TryGetProperty("Foul", out var fv) && fv.ValueKind == JsonValueKind.True) foul = true;
                        }
                        catch { }

                        string laneKey = laneIndex == 0 ? "left" : laneIndex == 1 ? "right" : null;
                        if (laneKey == null)
                        {
                            AppendLog($"HandleStartLineRunStartAsync: unknown lane index {laneIndex}, skipping");
                            continue;
                        }

                        var buf = GetBufferForLane(laneKey);
                        lock (buf)
                        {
                            buf.NoVehicle = noVehicle;
                            buf.DsFoul = dsFoul;
                            buf.Foul = foul;
                            if (noVehicle)
                            {
                                // Clear any detection/published markers so nothing is recorded for this lane
                                buf.LastPublishedRunId = Guid.Empty;
                                buf.DetectionTimestampNs = null;
                                buf.TrapStartTimestamps.Clear();
                                buf.TrapEndTimestamps.Clear();
                                buf.SeenDowntrackInputs.Clear();
                                buf.LastDowntrackDistanceMm = null;
                                buf.LastDowntrackTimestampNs = null;
                                // Also clear buffered guard/stage events
                                buf.GuardA.Clear();
                                buf.GuardB.Clear();
                                buf.StageEvents.Clear();
                                AppendLog($"HandleStartLineRunStartAsync: lane={laneKey} set NoVehicle=true — cleared buffers");
                            }
                            else
                            {
                                AppendLog($"HandleStartLineRunStartAsync: lane={laneKey} set NoVehicle=false");
                            }
                        }
                        // Notify subscribers (ViewModel) that DSFoul has changed for this lane
                        try
                        {
                            LaneDsFoulChanged?.Invoke(laneKey, dsFoul);
                        }
                        catch (Exception ex)
                        {
                            AppendLog("LaneDsFoulChanged handler threw: " + ex.Message);
                        }
                        // (No separate event for Foul here; Foul remark is generated from RT negativity)
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("HandleStartLineRunStartAsync parse error: " + ex.Message);
            }
            return Task.CompletedTask;
        }

        // Handle lanestart topic messages, e.g. lanestart/left/source with payload "Stage" or "Guard"
        private Task HandleLaneStartSourceAsync(string topic, string payload)
        {
            AppendLog($"HandleLaneStartSourceAsync: topic={topic} payload={payload}");
            try
            {
                // Determine lane from topic parts: expected lanestart/{left|right}/...
                string laneKey = null;
                try
                {
                    var parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        if (parts[1].Equals("left", StringComparison.OrdinalIgnoreCase)) laneKey = "left";
                        else if (parts[1].Equals("right", StringComparison.OrdinalIgnoreCase)) laneKey = "right";
                    }
                }
                catch { }

                // If lane not in topic, try to parse from JSON payload (optional)
                if (laneKey == null && !string.IsNullOrWhiteSpace(payload))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(payload);
                        if (doc.RootElement.TryGetProperty("Lane", out var l))
                        {
                            if (l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out var li)) laneKey = li == 0 ? "left" : li == 1 ? "right" : null;
                            else if (l.ValueKind == JsonValueKind.String)
                            {
                                var s = l.GetString();
                                if (s != null && s.Equals("left", StringComparison.OrdinalIgnoreCase)) laneKey = "left";
                                if (s != null && s.Equals("right", StringComparison.OrdinalIgnoreCase)) laneKey = "right";
                            }
                        }
                    }
                    catch { }
                }

                if (laneKey == null)
                {
                    AppendLog("HandleLaneStartSourceAsync: could not determine lane; ignoring");
                    return Task.CompletedTask;
                }

                // Parse source value
                string sourceVal = null;
                if (!string.IsNullOrWhiteSpace(payload))
                {
                    var trimmed = payload.Trim();
                    if ((trimmed.StartsWith("\"") && trimmed.EndsWith("\"")) || (trimmed.StartsWith("'") && trimmed.EndsWith("'")))
                        trimmed = trimmed.Substring(1, trimmed.Length - 2);

                    if (trimmed.StartsWith("{"))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(trimmed);
                            if (doc.RootElement.TryGetProperty("Source", out var s) || doc.RootElement.TryGetProperty("source", out s))
                            {
                                if (s.ValueKind == JsonValueKind.String) sourceVal = s.GetString();
                            }
                        }
                        catch { }
                    }

                    if (sourceVal == null) sourceVal = trimmed;
                }

                if (string.IsNullOrWhiteSpace(sourceVal))
                {
                    AppendLog("HandleLaneStartSourceAsync: no source value provided; ignoring");
                    return Task.CompletedTask;
                }

                sourceVal = sourceVal.Trim().ToLowerInvariant();

                var buf = GetBufferForLane(laneKey);
                // Copy buffered events for analysis
                List<(long ts, bool dir)> aCopy, bCopy; List<(long ts, bool dir)> stageCopy;
                lock (buf)
                {
                    aCopy = buf.GuardA.ToList();
                    bCopy = buf.GuardB.ToList();
                    stageCopy = buf.StageEvents.ToList();
                }

                // Determine guard enabled flags
                bool guardAEnabled = false, guardBEnabled = false;
                try
                {
                    if (_inputMapService != null)
                    {
                        if (laneKey == "left")
                        {
                            _inputMapService.TryGetIdentifier(InputRole.LeftGuardA, out var gA);
                            _inputMapService.TryGetIdentifier(InputRole.LeftGuardB, out var gB);
                            guardAEnabled = (gA != null && gA.Enabled);
                            guardBEnabled = (gB != null && gB.Enabled);
                        }
                        else
                        {
                            _inputMapService.TryGetIdentifier(InputRole.RightGuardA, out var gA);
                            _inputMapService.TryGetIdentifier(InputRole.RightGuardB, out var gB);
                            guardAEnabled = (gA != null && gA.Enabled);
                            guardBEnabled = (gB != null && gB.Enabled);
                        }
                    }
                    else
                    {
                        guardAEnabled = (laneKey == "left") ? (_leftGuardAId != null && _leftGuardAId.Enabled) : (_rightGuardAId != null && _rightGuardAId.Enabled);
                        guardBEnabled = (laneKey == "left") ? (_leftGuardBId != null && _leftGuardBId.Enabled) : (_rightGuardBId != null && _rightGuardBId.Enabled);
                    }
                }
                catch (Exception ex)
                {
                    AppendLog("HandleLaneStartSourceAsync: error querying InputMapService: " + ex.Message);
                }

                long? stageStartTs = null;
                if (sourceVal.Contains("stage"))
                {
                    var firstRemade = stageCopy.Where(x => x.dir).OrderBy(x => x.ts).Select(x => x.ts).FirstOrDefault();
                    stageStartTs = firstRemade == 0 ? (long?)null : firstRemade;
                }

                if (_runManager == null || string.IsNullOrEmpty(_currentRunDevice) || !_currentRunInput.HasValue)
                {
                    AppendLog("HandleLaneStartSourceAsync: no current run or RunManager not attached; ignoring");
                    lock (buf) buf.RecordingForLanestart = false;
                    return Task.CompletedTask;
                }

                if (_runManager.TryComputeReactionTime(_currentRunDevice, _currentRunInput.Value, stageStartTs, aCopy, bCopy, guardAEnabled, guardBEnabled, out var rtNs, out var runId, out var detectionSource, out var detectionTimestampNs))
                {
                    lock (buf)
                    {
                        if (!buf.NoVehicle)
                        {
                                if (buf.LastPublishedRunId != runId)
                                {
                                    buf.LastPublishedRunId = runId;
                                    // Defer publishing to ViewModel to ensure reactionTimeString matches UI (uses ExpectedReactionTimeNs)
                                    AppendLog($"Computed reaction time (lanestart) lane={laneKey} rt_ns={rtNs} runId={runId} source={detectionSource} (publish deferred to ViewModel)");
                                    buf.DetectionTimestampNs = detectionTimestampNs;
                                    try { ReactionTimeComputed?.Invoke(laneKey, rtNs, runId, detectionSource, detectionTimestampNs); } catch (Exception ex) { AppendLog("ReactionTimeComputed handler threw: " + ex.Message); }
                                }
                        }
                        else
                        {
                            AppendLog($"RT suppressed for lane={laneKey} because NoVehicle=true");
                        }

                        // Stop recording and clear buffers for this lane now that lanestart source is known
                        buf.RecordingForLanestart = false;
                        buf.StageEvents.Clear();
                        buf.GuardA.Clear();
                        buf.GuardB.Clear();
                    }
                }
                else
                {
                    AppendLog($"HandleLaneStartSourceAsync: TryComputeReactionTime returned false for lane={laneKey}");
                    lock (buf) buf.RecordingForLanestart = false;
                }
            }
            catch (Exception ex)
            {
                AppendLog("HandleLaneStartSourceAsync error: " + ex.Message);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Publish a message to MQTT broker.
        /// Follows pattern from samples using MqttApplicationMessageBuilder.
        /// </summary>
        public async Task PublishMqtt(string topic, string payload)
        {
            AppendLog($"PublishMqtt: topic={topic} payload_len={payload?.Length ?? 0}");
            
            try 
            { 
                File.AppendAllText(
                    Path.Combine(AppContext.BaseDirectory ?? ".", "mqtt_outgoing.log"), 
                    $"{DateTime.Now:o} {topic} {payload ?? string.Empty}\n"
                ); 
            } 
            catch { }

            try
            {
                await EnsureConnectedAsync();

                if (_mqttClient == null)
                {
                    AppendLog("PublishMqtt: no client available");
                    return;
                }

                // Build message using builder pattern (like samples)
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(false)
                    .Build();

                await _sync.WaitAsync();
                try
                {
                    var result = await _mqttClient.PublishAsync(message, CancellationToken.None);
                    AppendLog($"PublishMqtt: published topic={topic} result={result.ReasonCode}");
                }
                finally
                {
                    _sync.Release();
                }
            }
            catch (Exception ex)
            {
                AppendLog($"PublishMqtt failed for topic={topic}: {ex.Message}");
            }
        }

        public async Task<bool> ForceConnectAsync()
        {
            try
            {
                await EnsureConnectedAsync();
                return _mqttClient?.IsConnected == true;
            }
            catch (Exception ex)
            {
                AppendLog($"ForceConnectAsync failed: {ex.Message}");
                return false;
            }
        }

        public string GetStatus()
        {
            try
            {
                var clientAttached = _mqttClient != null;
                var optionsAttached = _options != null;
                var isConnected = _mqttClient?.IsConnected == true;
                return $"clientAttached={clientAttached} optionsAttached={optionsAttached} isConnected={isConnected}";
            }
            catch (Exception ex)
            {
                return $"GetStatus error: {ex.Message}";
            }
        }

        /// <summary>
        /// Clean disconnect following Clean_Disconnect() sample pattern.
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                // Stop reconnection monitor
                _reconnectCts?.Cancel();
                if (_reconnectTask != null)
                    await _reconnectTask;

                if (_mqttClient?.IsConnected == true)
                {
                    // Send clean disconnect packet (like sample)
                    var disconnectOptions = _mqttFactory.CreateClientDisconnectOptionsBuilder()
                        .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                        .Build();

                    await _mqttClient.DisconnectAsync(disconnectOptions, CancellationToken.None);
                    AppendLog("DisconnectAsync: clean disconnect completed");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"DisconnectAsync error: {ex.Message}");
            }
        }

        // Domain-specific publish methods (unchanged)
        public Task PubQueueRacersAsync(RaceEntry entry)
        {
            AppendLog($"PubQueueRacersAsync: lane={entry?.Lane} queueIndex={entry?.QueueIndex} race={entry?.RaceNumber}");
            try
            {
                var pairType = entry.QueueIndex switch 
                { 
                    0 => "enterpair", 
                    1 => "queuedpair", 
                    2 => "engagedpair", 
                    _ => UnknownPair 
                };
                
                var side = entry.Lane switch 
                { 
                    0 => "left", 
                    1 => "right", 
                    _ => "unknown" 
                };
                
                if (pairType == UnknownPair || side == "unknown") 
                    return Task.CompletedTask;

                var topic = $"runqueue/{pairType}/{side}";
                var payloadJson = JsonSerializer.Serialize(new 
                { 
                    raceNumber = entry.RaceNumber, 
                    handicapIndex = entry.HandicapIndex, 
                    tree = entry.Tree?.Id 
                });
                
                var publishMain = PublishMqtt(topic, payloadJson);

                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var detailPayload = JsonSerializer.Serialize(entry, options);
                var detailTopic = $"{topic}/detail";
                var publishDetail = PublishMqtt(detailTopic, detailPayload);

                return Task.WhenAll(publishMain, publishDetail);
            }
            catch (Exception ex)
            {
                AppendLog($"PubQueueRacersAsync failed: {ex.Message}");
                return Task.CompletedTask;
            }
        }

        public Task PubQueueCategAsync(CategQueueItem categ)
        {
            AppendLog($"PubQueueCategAsync: queueIndex={categ?.QueueIndex} category={categ?.Category}");
            try
            {
                var categType = categ.QueueIndex switch 
                { 
                    0 => "enterpair", 
                    1 => "queuedpair", 
                    2 => "engagedpair", 
                    _ => UnknownPair 
                };
                
                if (categType == UnknownPair) 
                    return Task.CompletedTask;

                var topic = $"runqueue/{categType}";
                var payloadJson = JsonSerializer.Serialize(new 
                { 
                    category = categ.Category, 
                    mode = categ.Mode, 
                    round = categ.Round, 
                    finish = categ.Finish 
                });
                
                var publishMain = PublishMqtt(topic, payloadJson);

                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var detailPayload = JsonSerializer.Serialize(categ, options);
                var detailTopic = $"{topic}/detail";
                var publishDetail = PublishMqtt(detailTopic, detailPayload);

                return Task.WhenAll(publishMain, publishDetail);
            }
            catch (Exception ex)
            {
                AppendLog($"PubQueueCategAsync failed: {ex.Message}");
                return Task.CompletedTask;
            }
        }

        public Task PubRunConfigAsync(CategQueueItem categ, IEnumerable<RaceEntry> racers)
        {
            AppendLog($"PubRunConfigAsync: category={categ?.Category} racers_count={racers?.Count()}");
            try
            {
                var list = racers.ToList();
                var left = list.FirstOrDefault(r => r.Lane == 0) 
                           ?? list.ElementAtOrDefault(0) 
                           ?? new RaceEntry { Lane = 0, QueueIndex = 2 };
                
                var right = list.FirstOrDefault(r => r.Lane == 1) 
                            ?? list.ElementAtOrDefault(1) 
                            ?? new RaceEntry { Lane = 1, QueueIndex = 2 };

                var leftPayload = JsonSerializer.Serialize(new 
                { 
                    seqType = left.Tree?.CountdownType, 
                    seqSpeed = left.Tree?.CountdownSpeed, 
                    index = left.HandicapIndex,
                });
                
                var rightPayload = JsonSerializer.Serialize(new 
                { 
                    seqType = right.Tree?.CountdownType, 
                    seqSpeed = right.Tree?.CountdownSpeed, 
                    index = right.HandicapIndex 
                });

                var publishLeft = PublishMqtt("runconfig/left", leftPayload);
                var publishRight = PublishMqtt("runconfig/right", rightPayload);

                var rDelay = new Random();
                
                var categoryPayload = JsonSerializer.Serialize(new 
                { 
                    runTimeout = categ?.CategoryDetails?.RunTimeout ?? 0, 
                    mode = categ?.Mode ?? 0,
                    deepStageFoul = categ?.CategoryDetails?.DeepStageFoul ?? false,
                    delayTime = rDelay.Next(categ?.CategoryDetails?.DelayMin ?? 0, categ?.CategoryDetails?.DelayMax ?? 0),
                    foulInEmpty = categ?.CategoryDetails?.FoulInEmpty ?? false
                });
                
                var publishCategory = PublishMqtt("runconfig/setup", categoryPayload);

                return Task.WhenAll(publishLeft, publishRight, publishCategory);
            }
            catch (Exception ex)
            {
                AppendLog($"PubRunConfigAsync failed: {ex.Message}");
                return Task.CompletedTask;
            }
        }

        public Task ResetSystemAsync() 
        { 
            AppendLog("ResetSystemAsync called"); 
            return PublishMqtt("runconfig/reset", "0"); 
        }

        public Task ConsoleStartAsync()
        {
            AppendLog("ConsoleStartAsync called");
            return PublishMqtt("runconfig/consolestart", "0");
        }

        public void Dispose()
        {
            _reconnectCts?.Cancel();
            _mqttClient?.Dispose();
        }
    }
}

