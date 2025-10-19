using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Text.Json;
using PulsarUI.Interfaces;
using PulsarUI.Models;

namespace PulsarUI.Services
{
    public class MqttService
    {
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
                _ => "unknownpair"
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
                racenumber = entry.RaceNumber,
                handicapindex = entry.HandicapIndex,
                tree = entry.Tree
            });

            return PublishMqtt(topic, payloadJson);
        }

        public Task PubQueueCategAsync(CategQueueItem categ)
        {
            var categType = categ.QueueIndex switch
            {
                0 => "enterpair",
                1 => "queuedpair",
                2 => "engagedpair",
                _ => "unknownpair"
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

            return PublishMqtt(topic, payloadJson);
        }
    }
}
