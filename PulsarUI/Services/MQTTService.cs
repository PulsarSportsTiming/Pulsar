using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Text.Json;
using System.Linq;
using PulsarUI.Models;
using System.Text;
using System;
using System.IO;
using System.Collections.Generic;

namespace PulsarUI.Services
{
    public class MqttService
    {
        // Event raised when a test message is received on the system/testmessage topic
        public event Action<string, string>? TestMessageReceived;

        // Simple diagnostic log helper (append-only)
        private static void AppendLog(string msg)
        {
            try
            {
                File.AppendAllText("/tmp/pulsar_mqtt.log", DateTime.Now.ToString("o") + " " + msg + "\n");
            }
            catch { }
        }

        private const string UnknownPair = "unknownpair";
        private readonly IMqttClient _mqttClient;
        private readonly MqttClientOptions _options;
        private readonly SemaphoreSlim _sync = new SemaphoreSlim(1, 1); // for thread safety

        // Dispatcher map for incoming topics -> handler
        private readonly Dictionary<string, Func<string, Task>> _topicHandlers = new Dictionary<string, Func<string, Task>>(StringComparer.OrdinalIgnoreCase);

        public MqttService()
        {
            AppendLog("MqttService ctor start");
            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();

            // Connection lifecycle handlers for diagnostics
            // Wire connection lifecycle handlers for diagnostics
            _mqttClient.ConnectedAsync += _ =>
            {
                AppendLog("MQTT client connected");
                return Task.CompletedTask;
            };

            _mqttClient.DisconnectedAsync += evt =>
            {
                // log full exception if present
                AppendLog($"MQTT client disconnected: {evt?.Exception?.Message}");
                return Task.CompletedTask;
            };

            // Register handlers for known topics
            RegisterTopicHandlers();

            // Subscribe to incoming application messages using the async callback available on IMqttClient in MQTTnet v4
            try
            {
                _mqttClient.ApplicationMessageReceivedAsync += args =>
                {
                    try
                    {
                        var topic = args.ApplicationMessage?.Topic ?? string.Empty;

                        // Use PayloadSegment to avoid using the obsolete Payload property
                        var seg = args.ApplicationMessage?.PayloadSegment ?? default;
                        string payload;
                        if (seg.Array == null || seg.Count == 0)
                        {
                            payload = string.Empty;
                        }
                        else
                        {
                            payload = Encoding.UTF8.GetString(seg.Array, seg.Offset, seg.Count);
                        }

                        AppendLog($"Received message topic={topic} payload={payload}");

                        // If there is a registered handler for this exact topic, call it
                        if (!string.IsNullOrEmpty(topic) && _topicHandlers.TryGetValue(topic, out var handler))
                        {
                            // Execute handler but don't block the mqtt callback thread; continue asynchronously
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await handler(payload).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    AppendLog($"Handler for '{topic}' failed: {ex}");
                                }
                            });
                        }
                        else if (string.Equals(topic, "system/testmessage", StringComparison.OrdinalIgnoreCase))
                        {
                            AppendLog("Invoking TestMessageReceived event");
                            TestMessageReceived?.Invoke(topic, payload);
                        }
                        else
                        {
                            AppendLog($"No handler registered for topic '{topic}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendLog("ApplicationMessageReceived handler error: " + ex);
                    }
                    return Task.CompletedTask;
                };
            }
            catch (Exception ex)
            {
                AppendLog("Failed to register ApplicationMessageReceivedAsync: " + ex);
            }

            _options = new MqttClientOptionsBuilder()
                .WithTcpServer("127.0.0.1", 1883)
                .WithClientId("pulsar")
                .WithCleanSession()
                .Build();

            // Attempt a background connect so the client is ready to receive messages and subscriptions are active
            // (EnsureConnectedAsync will also be called lazily by PublishMqtt, but for incoming subscriptions we want an active connection)
            try
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        AppendLog("Background connect task starting");
                        await EnsureConnectedAsync().ConfigureAwait(false);
                        AppendLog("Background connect task finished");
                    }
                    catch (Exception ex)
                    {
                        // best-effort: swallow — connection failures will be retried when publishing
                        AppendLog("Background connect failed: " + ex.Message);
                    }
                });
            }
            catch { }
        }

        private async Task EnsureConnectedAsync()
        {
            AppendLog("EnsureConnectedAsync called");
            if (_mqttClient.IsConnected) 
            {
                AppendLog("Already connected");
                 return;
            }

            await _sync.WaitAsync();
            try
            {
                if (!_mqttClient.IsConnected) // double-check after acquiring lock
                {
                    AppendLog("Connecting to broker...");
                    await _mqttClient.ConnectAsync(_options);
                    AppendLog("ConnectAsync returned");

                    // Subscribe to the configured set of topics after connecting. Ignore errors.
                    try
                    {
                        var topics = new[]
                        {
                            "system/testmessage",
                            "inputs/status",
                            "input/timestamps",
                            "apibridge/competitors",
                            "apibridge/bumpspot",
                            "apibridge/categories",
                            "apibridge/classes"
                        };

                        // Build subscribe options compatible with MQTTnet v4
                        var subBuilder = new MqttClientSubscribeOptionsBuilder();
                        foreach (var t in topics) subBuilder.WithTopicFilter(t);
                        var subscribeOptions = subBuilder.Build();
                        await _mqttClient.SubscribeAsync(subscribeOptions).ConfigureAwait(false);
                        AppendLog("SubscribeAsync succeeded for configured topics");
                    }
                    catch (Exception ex) { AppendLog("SubscribeAsync failed: " + ex.Message); }
                }
            }
            finally
            {
                _sync.Release();
            }
        }

        // Register topic handlers (exact-match topics)
        private void RegisterTopicHandlers()
        {
            _topicHandlers["inputs/status"] = HandleInputsStatusAsync;
            _topicHandlers["input/timestamps"] = HandleInputTimestampsAsync;
            _topicHandlers["apibridge/competitors"] = HandleApiCompetitorsAsync;
            _topicHandlers["apibridge/bumpspot"] = HandleApiBumpSpotAsync;
            _topicHandlers["apibridge/categories"] = HandleApiCategoriesAsync;
            _topicHandlers["apibridge/classes"] = HandleApiClassesAsync;
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
            try
            {
                using var doc = JsonDocument.Parse(payload);
                // TODO: process timestamps
            }
            catch (Exception ex)
            {
                AppendLog("input/timestamps parse error: " + ex.Message);
            }
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

        public async Task PublishMqtt(string topic, string payload)
        {
            await EnsureConnectedAsync();

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _sync.WaitAsync(); // prevent concurrent PublishAsync calls
            try
            {
                await _mqttClient.PublishAsync(message);
            }
            finally
            {
                _sync.Release();
            }
        }

        public Task PubQueueRacersAsync(RaceEntry entry)
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

            if (pairType == "unknownpair" || side == "unknown")
                return Task.CompletedTask;

            var topic = $"runqueue/{pairType}/{side}";

            var payloadJson = JsonSerializer.Serialize(new
            {
                raceNumber = entry.RaceNumber,
                handicapIndex = entry.HandicapIndex,
                tree = entry.Tree?.Id
            });

            // Publish the minimal payload as before
            var publishMain = PublishMqtt(topic, payloadJson);

            // Also publish a detailed payload on the ".../detail" topic with all properties (camelCase)
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var detailPayload = JsonSerializer.Serialize(entry, options);
            var detailTopic = topic + "/detail";
            var publishDetail = PublishMqtt(detailTopic, detailPayload);

            return Task.WhenAll(publishMain, publishDetail);
        }

        public Task PubQueueCategAsync(CategQueueItem categ)
        {
            var categType = categ.QueueIndex switch
            {
                0 => "enterpair",
                1 => "queuedpair",
                2 => "engagedpair",
                _ => UnknownPair
            };

            if (categType == "unknownpair")
                return Task.CompletedTask;

            var topic = $"runqueue/{categType}";

            var payloadJson = JsonSerializer.Serialize(new
            {
                category = categ.Category,
                mode = categ.Mode,
                round = categ.Round,
                finish = categ.Finish
            });

            // Publish the minimal payload as before
            var publishMain = PublishMqtt(topic, payloadJson);

            // Also publish a detailed payload on the ".../detail" topic with all properties (camelCase)
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var detailPayload = JsonSerializer.Serialize(categ, options);
            var detailTopic = topic + "/detail";
            var publishDetail = PublishMqtt(detailTopic, detailPayload);

            return Task.WhenAll(publishMain, publishDetail);
        }

        // Consolidated run-config publisher: publishes category-level config to "runconfig"
        // and lane-specific settings to "runconfig/left" and "runconfig/right".
        
        public Task ResetSystemAsync()
        {
            return PublishMqtt("runconfig/reset", "0");
        }
        public Task PubRunConfigAsync(CategQueueItem categ, System.Collections.Generic.IEnumerable<RaceEntry> racers)
        {
            // 'categ' is non-nullable by signature, so skip defensive null-checks.

            // Materialize racers into a list once to avoid multiple enumeration
            var list = racers.ToList();

            // Prefer lane-based selection, fallback to positional indices, finally placeholders
            var left = list.FirstOrDefault(r => r.Lane == 0)
                       ?? list.ElementAtOrDefault(0)
                       ?? new RaceEntry { Lane = 0, QueueIndex = 2 };

            var right = list.FirstOrDefault(r => r.Lane == 1)
                        ?? list.ElementAtOrDefault(1)
                        ?? new RaceEntry { Lane = 1, QueueIndex = 2 };

            // Build and publish lane-specific minimal payloads to distinct topics
            var leftPayload = JsonSerializer.Serialize(new
            {
                seqType = left.Tree?.CountdownType,
                seqSpeed = left.Tree?.CountdownSpeed,
                index = left.HandicapIndex
            });
            var rightPayload = JsonSerializer.Serialize(new
            {
                seqType = right.Tree?.CountdownType,
                seqSpeed = right.Tree?.CountdownSpeed,
                index = right.HandicapIndex
            });

            var publishLeft = PublishMqtt("runconfig/left", leftPayload);
            var publishRight = PublishMqtt("runconfig/right", rightPayload);

            // Build and publish category-level config to "runconfig" (keeps prior behavior)
            var categoryPayload = JsonSerializer.Serialize(new
            {
                runTimeout = categ.CategoryDetails.RunTimeout,
                bumpEt = categ.CategoryDetails.BumpEt,
                mode = categ.Mode,
                staggered = categ.CategoryDetails.StaggeredStartsAllowed,
                startMode = categ.CategoryDetails.StartMode,
                stageFreeze = categ.CategoryDetails.StageFreeze,
                deepStageFoul = categ.CategoryDetails.DeepStageFoul,
                sbElimSpeed = categ.CategoryDetails.SbElimSpeed,
                sbCycleUnits = categ.CategoryDetails.SbCycleUnits,
                worstFoul = categ.CategoryDetails.WorstFoul,
                foulInEmpty = categ.CategoryDetails.FoulInEmpty,
                stageSettle = categ.CategoryDetails.StageSettle,
                autoStartStageToStart = categ.CategoryDetails.AutoStartStageToStart,
                autoStartVariance = categ.CategoryDetails.AutoStartVariance,
                autoStartTimeout = categ.CategoryDetails.AutoStartTimeout
            });
            var publishCategory = PublishMqtt("runconfig", categoryPayload);

            return Task.WhenAll(publishLeft, publishRight, publishCategory);
        }
    }
}
