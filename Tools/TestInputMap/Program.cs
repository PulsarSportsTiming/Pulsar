using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PulsarUI.Models;
using PulsarUI.Services;

namespace TestInputMap
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var candidatePaths = new[]
                {
                    Path.Combine(baseDir, "Config", "inputmap.json"),
                    Path.Combine(baseDir, "..", "..", "..", "Config", "inputmap.json"),
                    Path.Combine(baseDir, "..", "..", "Config", "inputmap.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Config", "inputmap.json")
                };

                string configPath = candidatePaths.FirstOrDefault(File.Exists)
                                    ?? Path.Combine(baseDir, "Config", "inputmap.json");

                if (!File.Exists(configPath))
                {
                    Console.Error.WriteLine("Config/inputmap.json not found. Tried:");
                    foreach (var p in candidatePaths) Console.Error.WriteLine("  " + p);
                    return 2;
                }

                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
                };

                var inputs = new List<DownTrackInput>();
                if (root.TryGetProperty("DownTrackInputs", out var dtiProp))
                {
                    inputs = JsonSerializer.Deserialize<List<DownTrackInput>>(dtiProp.GetRawText(), options) ?? new List<DownTrackInput>();
                }

                var traps = new List<SpeedTrap>();
                if (root.TryGetProperty("SpeedTraps", out var stProp))
                {
                    traps = JsonSerializer.Deserialize<List<SpeedTrap>>(stProp.GetRawText(), options) ?? new List<SpeedTrap>();
                }

                Console.WriteLine($"Loaded {inputs.Count} DownTrackInputs and {traps.Count} SpeedTraps from {configPath}\n");

                var labelsPerLane = TimingLabelHelpers.GenerateTimingLabels(inputs, traps, "mi", "mph");

                foreach (var kv in labelsPerLane.OrderBy(k => (int)k.Key))
                {
                    Console.WriteLine($"Lane {(int)kv.Key} ({kv.Key}):");
                    foreach (var l in kv.Value)
                        Console.WriteLine("  " + l);
                    Console.WriteLine();
                }

                bool ok = true;
                foreach (var kv in labelsPerLane)
                {
                    var list = kv.Value;
                    bool found = false;
                    for (int i = 0; i < list.Count - 1; i++)
                    {
                        if (list[i] == "1/8 mi ET" && list[i + 1] == "1/8 mi mph")
                        {
                            Console.WriteLine($"Verified 1/8 sequence in lane {(int)kv.Key}.");
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        Console.Error.WriteLine($"Failed to verify 1/8 mi sequence in lane {(int)kv.Key}.");
                        ok = false;
                    }
                }

                if (!ok)
                {
                    Console.Error.WriteLine("One or more lane verifications failed.");
                    return 3;
                }

                Console.WriteLine("All checks passed.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Exception: " + ex);
                return 1;
            }
        }
    }
}
