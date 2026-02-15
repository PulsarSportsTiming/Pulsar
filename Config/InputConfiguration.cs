using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PulsarUI.Models;
using System.IO;

namespace PulsarUI.Config
{
    public class InputConfiguration
    {
        // Remove per-property JsonConverter attribute (incompatible with Dictionary<enum, T>);
        // the JsonStringEnumConverter is already added to the JsonSerializerOptions in LoadFromJson.
        public Dictionary<InputRole, InputIdentifier> RoleMap { get; set; } = new();

        public List<DownTrackInput> DownTrackInputs { get; set; } = new();
        public List<SpeedTrap> SpeedTraps { get; set; } = new();

        private Dictionary<string, InputRole>? _reverseByDeviceInput;

        public void BuildLookup()
        {
            _reverseByDeviceInput = RoleMap
                .Where(kv => kv.Value != null)
                .ToDictionary(kv => Key(kv.Value), kv => kv.Key, StringComparer.OrdinalIgnoreCase);
        }

        private static string Key(InputIdentifier id) => $"{id.Device}:{id.InputIndex}";

        public bool TryGetRole(InputIdentifier id, out InputRole role)
        {
            if (_reverseByDeviceInput == null) BuildLookup();
            return _reverseByDeviceInput!.TryGetValue(Key(id), out role);
        }

        public bool TryGetIdentifier(InputRole role, out InputIdentifier id)
        {
            if (RoleMap.TryGetValue(role, out var tmp) && tmp != null)
            {
                id = tmp;
                return true;
            }

            // Fallback: case-insensitive lookup by enum name (covers edge cases)
            var roleName = role.ToString();
            foreach (var kv in RoleMap)
            {
                if (kv.Key.ToString().Equals(roleName, StringComparison.OrdinalIgnoreCase) && kv.Value != null)
                {
                    id = kv.Value;
                    return true;
                }
            }

            id = new InputIdentifier();
            return false;
        }

        public IEnumerable<DownTrackInput> GetDownTrackInputsForDevice(string device)
        {
            if (string.IsNullOrWhiteSpace(device)) return Enumerable.Empty<DownTrackInput>();
            var normalized = device.Trim();
            return DownTrackInputs
                .Where(d => d != null && d.Id != null && !string.IsNullOrWhiteSpace(d.Id.Device))
                .Where(d => string.Equals(d.Id.Device.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
        }

        public static InputConfiguration LoadFromJson(string json)
        {
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };

            // First attempt: standard deserialization
            InputConfiguration cfg;
            try
            {
                cfg = JsonSerializer.Deserialize<InputConfiguration>(json, opts) ?? new InputConfiguration();
            }
            catch
            {
                // If standard deserialization fails, fall back to an empty configuration and continue with manual parsing
                cfg = new InputConfiguration();
            }

            // Robustly parse RoleMap keys from the raw JSON: handle string enum names or numeric keys.
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    // Case-insensitive discovery of "roleMap" property
                    JsonElement roleMapEl = default;
                    bool foundRoleMap = false;
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        if (string.Equals(p.Name, "roleMap", StringComparison.OrdinalIgnoreCase))
                        {
                            roleMapEl = p.Value;
                            foundRoleMap = true;
                            break;
                        }
                    }

                    if (foundRoleMap && roleMapEl.ValueKind == JsonValueKind.Object)
                    {
                        var map = new Dictionary<InputRole, InputIdentifier>();
                        foreach (var prop in roleMapEl.EnumerateObject())
                        {
                            var key = prop.Name.Trim();
                            // Try parse as enum name (case-insensitive)
                            if (Enum.TryParse<InputRole>(key, ignoreCase: true, out var roleKey))
                            {
                                try
                                {
                                    // If the value is an array, prefer the first element, otherwise deserialize a single InputIdentifier
                                    InputIdentifier id;
                                    if (prop.Value.ValueKind == JsonValueKind.Array)
                                    {
                                        var ids = JsonSerializer.Deserialize<List<InputIdentifier>>(prop.Value.GetRawText(), opts);
                                        id = (ids != null && ids.Count > 0) ? ids[0] : new InputIdentifier();
                                    }
                                    else
                                    {
                                        id = JsonSerializer.Deserialize<InputIdentifier>(prop.Value.GetRawText(), opts) ?? new InputIdentifier();
                                    }
                                    map[roleKey] = id;
                                }
                                catch (Exception ex)
                                {
                                    Console.Error.WriteLine("InputConfiguration.Load: malformed RoleMap entry (enum name) for key='" + key + "': " + ex.Message);
                                }
                                continue;
                            }

                            var baseRoleNames = new[] { "PreStage", "StageLock", "Stage", "GuardA", "GuardB" };
                            if (Array.Exists(baseRoleNames, b => string.Equals(b, key, StringComparison.OrdinalIgnoreCase)) &&
                                prop.Value.ValueKind == JsonValueKind.Array)
                            {
                                int elIndex = 0;
                                foreach (var el in prop.Value.EnumerateArray())
                                {
                                    try
                                    {
                                        int? laneNum = null;
                                        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("lane", out var laneProp))
                                        {
                                            if (laneProp.ValueKind == JsonValueKind.Number && laneProp.TryGetInt32(out var ln)) laneNum = ln;
                                            // If you want to accept numeric-as-string too, you could add:
                                            // else if (laneProp.ValueKind == JsonValueKind.String && int.TryParse(laneProp.GetString(), out var ln2)) laneNum = ln2;
                                        }

                                        if (laneNum == null)
                                        {
                                            Console.Error.WriteLine($"InputConfiguration.Load: RoleMap '{key}' element[{elIndex}] missing or invalid numeric 'lane' field; skipping element.");
                                            elIndex++;
                                            continue;
                                        }

                                        if (laneNum != 0 && laneNum != 1)
                                        {
                                            Console.Error.WriteLine($"InputConfiguration.Load: RoleMap '{key}' element[{elIndex}] has unsupported lane value '{laneNum}'; expected 0 or 1; skipping.");
                                            elIndex++;
                                            continue;
                                        }

                                        var roleName = (laneNum == 0 ? "Left" : "Right") + key;
                                        if (!Enum.TryParse<InputRole>(roleName, ignoreCase: true, out var parsedRole))
                                        {
                                            Console.Error.WriteLine($"InputConfiguration.Load: could not parse role name '{roleName}' from base key '{key}' element[{elIndex}]; skipping element.");
                                            elIndex++;
                                            continue;
                                        }

                                        var id = JsonSerializer.Deserialize<InputIdentifier>(el.GetRawText(), opts) ?? new InputIdentifier();
                                        map[parsedRole] = id;
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.Error.WriteLine($"InputConfiguration.Load: malformed RoleMap base-key array entry for key='{key}' element[{elIndex}]: " + ex.Message);
                                    }
                                    elIndex++;
                                }
                                continue;
                            }

                            // Try parse as numeric value
                            if (int.TryParse(key, out var numeric))
                            {
                                if (Enum.IsDefined(typeof(InputRole), numeric))
                                {
                                    var rk = (InputRole)numeric;
                                    try
                                    {
                                        InputIdentifier id;
                                        if (prop.Value.ValueKind == JsonValueKind.Array)
                                        {
                                            var ids = JsonSerializer.Deserialize<List<InputIdentifier>>(prop.Value.GetRawText(), opts);
                                            id = (ids != null && ids.Count > 0) ? ids[0] : new InputIdentifier();
                                        }
                                        else
                                        {
                                            id = JsonSerializer.Deserialize<InputIdentifier>(prop.Value.GetRawText(), opts) ?? new InputIdentifier();
                                        }
                                        map[rk] = id;
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.Error.WriteLine("InputConfiguration.Load: malformed RoleMap entry (numeric key) for key='" + key + "': " + ex.Message);
                                    }
                                }
                            }
                        }
                        // If we parsed any entries, override the RoleMap to ensure keys exist
                        if (map.Count > 0)
                            cfg.RoleMap = map;
                    }

                    // Case-insensitive parse of DownTrackInputs: some JSON files may use different casing.
                    try
                    {
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            if (string.Equals(prop.Name, "downtrackinputs", StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    var list = JsonSerializer.Deserialize<List<DownTrackInput>>(prop.Value.GetRawText(), opts) ?? new List<DownTrackInput>();
                                    if (list.Count > 0) cfg.DownTrackInputs = list;
                                    // (removed verbose local diagnostic writes)
                                }
                                catch (Exception ex)
                                {
                                    Console.Error.WriteLine("InputConfiguration.Load: malformed DownTrackInputs entry: " + ex.Message);
                                }
                                // removed break here so SpeedTraps (which may appear after) are still processed
                            }
                            // Also support SpeedTraps entry
                            if (string.Equals(prop.Name, "speedtraps", StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    var list = JsonSerializer.Deserialize<List<SpeedTrap>>(prop.Value.GetRawText(), opts) ?? new List<SpeedTrap>();
                                    if (list.Count > 0) cfg.SpeedTraps = list;
                                }
                                catch (Exception ex)
                                {
                                    Console.Error.WriteLine("InputConfiguration.Load: malformed SpeedTraps entry: " + ex.Message);
                                }
                                // do not break here; allow DownTrackInputs parsing to continue if present
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("InputConfiguration.Load: DownTrackInputs parse error: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("InputConfiguration.Load: JSON parse error: " + ex.Message);
            }

            cfg.BuildLookup();
            return cfg;
        }
    }
}
