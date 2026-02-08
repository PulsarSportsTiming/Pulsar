using System;
using System.Threading.Tasks;
using PulsarUI.Services;

class Program
{
    static async Task Main()
    {
        var svc = new MqttService();
        // Call a publish method that logs to mqtt_outgoing.log
        await svc.PublishMqtt("test/topic", "hello world");
        Console.WriteLine("Published test message (check mqtt_outgoing.log)");
    }
}
