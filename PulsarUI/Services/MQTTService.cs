using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Text.Json;
using System.Linq;
using PulsarUI.Models;

namespace PulsarUI.Services
{
    public class MqttService
    {
        private const string UnknownPair = "unknownpair";
        private readonly IMqttClient _mqttClient;
        private readonly MqttClientOptions _options;
        private readonly SemaphoreSlim _sync = new SemaphoreSlim(1, 1); // for thread safety

        public MqttService()
        {
            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();

            _options = new MqttClientOptionsBuilder()
                .WithTcpServer("127.0.0.1", 1883)
                .WithClientId("pulsar")
                .WithCleanSession()
                .Build();
        }

        private async Task EnsureConnectedAsync()
        {
            if (_mqttClient.IsConnected) 
                return;

            await _sync.WaitAsync();
            try
            {
                if (!_mqttClient.IsConnected) // double-check after acquiring lock
                {
                    await _mqttClient.ConnectAsync(_options);
                }
            }
            finally
            {
                _sync.Release();
            }
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
