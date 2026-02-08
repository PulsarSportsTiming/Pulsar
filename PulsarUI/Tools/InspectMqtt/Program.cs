using System;
using System.IO;
using System.Linq;
using System.Reflection;

Console.WriteLine("Inspecting MQTTnet assembly in local NuGet package folder (if found)");

var userProfile = Environment.GetEnvironmentVariable("HOME") ?? ".";
var nugetFolder = Path.Combine(userProfile, ".nuget", "packages", "mqttnet");
if (!Directory.Exists(nugetFolder))
{
    Console.WriteLine($"NuGet mqttnet package folder not found at {nugetFolder}");
    return;
}

var versions = Directory.GetDirectories(nugetFolder).OrderByDescending(d => d).ToList();
if (versions.Count == 0)
{
    Console.WriteLine("No mqttnet versions installed in NuGet cache");
    return;
}

foreach (var verPath in versions)
{
    var libPath = Path.Combine(verPath, "lib");
    if (!Directory.Exists(libPath)) continue;
    var dlls = Directory.GetFiles(libPath, "MQTTnet.dll", SearchOption.AllDirectories);
    foreach (var dll in dlls)
    {
        Console.WriteLine($"Found MQTTnet.dll at: {dll}");
        try
        {
            var asm = Assembly.LoadFrom(dll);
            Console.WriteLine($"Loaded assembly: {asm.FullName}");
            var types = asm.GetTypes().OrderBy(t => t.FullName);
            foreach (var t in types)
            {
                Console.WriteLine(t.FullName);
                if (t.Name.IndexOf("MqttFactory", StringComparison.OrdinalIgnoreCase) >= 0 || t.Name.IndexOf("MqttClient", StringComparison.OrdinalIgnoreCase) >= 0 || t.Name.IndexOf("MqttClientOptionsBuilder", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine("  Constructors:");
                    foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        Console.WriteLine("    " + c.ToString());
                    }
                    Console.WriteLine("  Methods:");
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                    {
                        if (m.Name.StartsWith("Create") || m.Name.StartsWith("With") || m.Name.StartsWith("Build") || m.Name.StartsWith("Publish") || m.Name.StartsWith("Connect"))
                        {
                            Console.WriteLine("    " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Load failed: " + ex.Message);
        }
    }
}

Console.WriteLine("Done.");
