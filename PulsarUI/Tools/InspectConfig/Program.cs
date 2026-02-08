using System;
using System.Reflection;
using System.IO;
using PulsarUI.Config;

var baseDir = AppContext.BaseDirectory;
var cwd = Directory.GetCurrentDirectory();
var candidates = new[]
{
    Path.Combine(cwd, "Config", "inputmap.json"),
    Path.Combine(cwd, "..", "Config", "inputmap.json"),
    Path.Combine(baseDir, "..", "..", "..", "Config", "inputmap.json"),
    Path.Combine(baseDir, "..", "..", "..", "..", "Config", "inputmap.json"),
    Path.Combine(baseDir, "Config", "inputmap.json")
};
string? found = null;
foreach (var c in candidates)
{
    var full = Path.GetFullPath(c);
    Console.WriteLine($"InspectConfig: checking {full}");
    if (File.Exists(full)) { found = full; break; }
}
if (found == null)
{
    Console.WriteLine("Config not found in any candidate paths.");
    return;
}
Console.WriteLine($"Looking for config at: {found}");
var json = File.ReadAllText(found);
try
{
    var cfg = InputConfiguration.LoadFromJson(json);
    Console.WriteLine($"Parsed RoleMap count: {cfg.RoleMap.Count}");
    foreach (var k in cfg.RoleMap.Keys) Console.WriteLine(k.ToString());
    Console.WriteLine($"Parsed DownTrackInputs count: {cfg.DownTrackInputs.Count}");
    foreach (var d in cfg.DownTrackInputs)
    {
        Console.WriteLine($"DTI: Device={d.Id?.Device} InputIndex={d.Id?.InputIndex} DistanceMm={d.DistanceMm} Lane={(int)d.Lane}");
    }
}
catch (Exception ex)
{
    Console.WriteLine("Load failed: " + ex.Message);
}