using System;
using System.IO;
using System.Linq;
using System.Threading;
using PulsarUI.Config;
using PulsarUI.Models;

namespace PulsarUI.Services
{
    public class InputMapService : IDisposable
    {
        private readonly string _path;
        private InputConfiguration _config;
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly System.IO.FileSystemWatcher? _watcher;

        public InputMapService(string jsonPath)
        {
            _path = jsonPath ?? throw new ArgumentNullException(nameof(jsonPath));
            _config = Load(_path);
            // Watch the file for changes so external edits (e.g., toggling enabled flags) are applied at runtime
            try
            {
                var dir = System.IO.Path.GetDirectoryName(_path) ?? ".";
                var fn = System.IO.Path.GetFileName(_path) ?? _path;
                _watcher = new System.IO.FileSystemWatcher(dir, fn)
                {
                    NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.CreationTime
                };
                _watcher.Changed += (s, e) => {
                    try { Reload(); } catch { }
                };
                _watcher.Renamed += (s, e) => { try { Reload(); } catch { } };
                _watcher.Deleted += (s, e) => { try { Reload(); } catch { } };
                _watcher.EnableRaisingEvents = true;
            }
            catch { /* best-effort: file watching is optional */ }
            // Emit a startup diagnostic indicating whether the file was found and how many DownTrackInputs were loaded
            try
            {
                var exists = File.Exists(_path);
                File.AppendAllText("/tmp/pulsarui_config.log", DateTime.Now.ToString("o") + " InputMapService ctor: path='" + _path + "' exists=" + exists + "\n");
                try
                {
                    var count = _config?.DownTrackInputs?.Count ?? 0;
                    File.AppendAllText("/tmp/pulsarui_config.log", DateTime.Now.ToString("o") + " InputMapService ctor: DownTrackInputs count=" + count + "\n");
                }
                catch { }
            }
            catch { }
        }

        private static InputConfiguration Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    try { File.AppendAllText("/tmp/pulsarui_config.log", DateTime.Now.ToString("o") + " InputMapService.Load: file does not exist at path='" + path + "'\n"); } catch { }
                    return new InputConfiguration();
                }

                var json = File.ReadAllText(path);
                try { File.AppendAllText("/tmp/pulsarui_config.log", DateTime.Now.ToString("o") + " InputMapService.Load: read " + json.Length + " bytes from '" + path + "'\n"); } catch { }
                return InputConfiguration.LoadFromJson(json);
            }
            catch (Exception ex)
            {
                try { File.AppendAllText("/tmp/pulsarui_config.log", DateTime.Now.ToString("o") + " InputMapService.Load: exception reading '" + path + "': " + ex.Message + "\n"); } catch { }
                return new InputConfiguration();
            }
        }

        public void Reload()
        {
            var cfg = Load(_path);
            _lock.EnterWriteLock();
            try { _config = cfg; }
            finally { _lock.ExitWriteLock(); }
        }

        public bool TryGetRole(InputIdentifier id, out InputRole role)
        {
            _lock.EnterReadLock();
            try { return _config.TryGetRole(id, out role); }
            finally { _lock.ExitReadLock(); }
        }

        public bool TryGetIdentifier(InputRole role, out InputIdentifier id)
        {
            _lock.EnterReadLock();
            try { return _config.TryGetIdentifier(role, out id); }
            finally { _lock.ExitReadLock(); }
        }

        // Tolerant lookup: normalize both configured device strings and the incoming device string
        // and return entries that either exactly match or end-with the normalized device (to allow prefixes).
        public DownTrackInput[] GetDownTrackInputsForDevice(string device)
        {
            _lock.EnterReadLock();
            try
            {
                if (string.IsNullOrWhiteSpace(device)) return Array.Empty<DownTrackInput>();
                string Normalize(string? d)
                {
                    if (string.IsNullOrWhiteSpace(d)) return string.Empty;
                    var s = d.Trim();
                    if (s.Length >= 2 && ((s.StartsWith("\"") && s.EndsWith("\"")) || (s.StartsWith("'") && s.EndsWith("'"))))
                        s = s.Substring(1, s.Length - 2);
                    return s;
                }

                var normalized = Normalize(device);
                var matches = _config.DownTrackInputs
                    .Where(d => d != null && d.Id != null)
                    .Where(d =>
                    {
                        var cfg = Normalize(d.Id.Device);
                        if (string.Equals(cfg, normalized, StringComparison.OrdinalIgnoreCase)) return true;
                        if (!string.IsNullOrEmpty(cfg) && cfg.EndsWith(normalized, StringComparison.OrdinalIgnoreCase)) return true;
                        return false;
                    })
                    .ToArray();
                return matches;
            }
            finally { _lock.ExitReadLock(); }
        }

        // Find a single DownTrackInput by device and input index using the tolerant matching rules.
        public DownTrackInput? TryFindDownTrackInput(string device, int inputIndex)
        {
            _lock.EnterReadLock();
            try
            {
                if (string.IsNullOrWhiteSpace(device)) return null;
                string Normalize(string? d)
                {
                    if (string.IsNullOrWhiteSpace(d)) return string.Empty;
                    var s = d.Trim();
                    if (s.Length >= 2 && ((s.StartsWith("\"") && s.EndsWith("\"")) || (s.StartsWith("'") && s.EndsWith("'"))))
                        s = s.Substring(1, s.Length - 2);
                    return s;
                }
                var normalized = Normalize(device);
                // Log the total configured DownTrackInputs and their normalized device strings for diagnosis
                try
                {
                    var all = _config.DownTrackInputs ?? new System.Collections.Generic.List<DownTrackInput>();
                    var sbAll = new System.Text.StringBuilder();
                    sbAll.AppendLine(DateTime.Now.ToString("o") + " InputMapService: total DownTrackInputs=" + all.Count + " (dump normalized devices)");
                    foreach (var dd in all)
                    {
                        sbAll.AppendLine("  cfg: '" + (dd?.Id?.Device ?? "<null>") + "' -> norm='" + Normalize(dd?.Id?.Device) + "' idx=" + dd?.Id?.InputIndex);
                    }
                    System.IO.File.AppendAllText("/tmp/pulsar_mqtt.log", sbAll.ToString());
                }
                catch { }
                var candidates = _config.DownTrackInputs
                    .Where(d => d != null && d.Id != null)
                    .Select(d => new { Item = d, DeviceNorm = Normalize(d.Id.Device) })
                    .Where(x =>
                    {
                        // Relaxed substring match both ways to tolerate minor formatting differences
                        if (string.IsNullOrEmpty(x.DeviceNorm) || string.IsNullOrEmpty(normalized)) return false;
                        if (x.DeviceNorm.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                        if (normalized.IndexOf(x.DeviceNorm, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                        return false;
                    })
                    .Select(x => x.Item)
                    .ToArray();

                // Diagnostic dump of normalized configured devices (always emit to help debugging)
                try
                {
                    var sbList = new System.Text.StringBuilder();
                    sbList.AppendLine(DateTime.Now.ToString("o") + " InputMapService: normalized configured devices dump (for lookup) -> incoming='" + normalized + "'");
                    foreach (var d in _config.DownTrackInputs.Where(d => d != null && d.Id != null))
                    {
                        sbList.AppendLine($"  cfg='{d.Id.Device}' -> norm='{Normalize(d.Id.Device)}' idx={d.Id.InputIndex}");
                    }
                    System.IO.File.AppendAllText("/tmp/pulsar_mqtt.log", sbList.ToString());
                }
                catch { }

                // If we found no candidates, attempt to reload the config from disk once and retry
                if (candidates.Length == 0)
                {
                    try
                    {
                        var before = _config.DownTrackInputs?.Count ?? 0;
                        var fileExists = File.Exists(_path);
                        if (fileExists)
                        {
                            var json = File.ReadAllText(_path);
                            var newCfg = InputConfiguration.LoadFromJson(json);
                            _config = newCfg;
                        }
                        var after = _config.DownTrackInputs?.Count ?? 0;
                        var sbR = new System.Text.StringBuilder();
                        sbR.AppendLine(DateTime.Now.ToString("o") + " InputMapService: Reload attempted in TryFindDownTrackInput; _path='" + _path + "' existed=" + fileExists + " beforeCount=" + before + " afterCount=" + after);
                        System.IO.File.AppendAllText("/tmp/pulsar_mqtt.log", sbR.ToString());
                        // Recompute candidates from reloaded config
                        candidates = _config.DownTrackInputs
                            .Where(d => d != null && d.Id != null)
                            .Select(d => new { Item = d, DeviceNorm = Normalize(d.Id.Device) })
                            .Where(x => string.Equals(x.DeviceNorm, normalized, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(x.DeviceNorm) && x.DeviceNorm.EndsWith(normalized, StringComparison.OrdinalIgnoreCase)))
                            .Select(x => x.Item)
                            .ToArray();
                    }
                    catch (Exception ex)
                    {
                        try { System.IO.File.AppendAllText("/tmp/pulsar_mqtt.log", DateTime.Now.ToString("o") + " InputMapService: reload in TryFindDownTrackInput failed: " + ex.Message + "\n"); } catch { }
                    }
                }

                // Diagnostic logging to help trace lookup failures
                try
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine(DateTime.Now.ToString("o") + " InputMapService.TryFindDownTrackInput: lookup device='" + device + "' normalized='" + normalized + "' inputIndex=" + inputIndex);
                    sb.AppendLine("Candidates count: " + candidates.Length);
                    foreach (var c in candidates)
                    {
                        sb.AppendLine($"  candidate Device='{c.Id.Device}' InputIndex={c.Id.InputIndex} DistanceMm={c.DistanceMm} Lane={(int)c.Lane}");
                    }
                    System.IO.File.AppendAllText("/tmp/pulsar_mqtt.log", sb.ToString());
                }
                catch { }

                if (candidates.Length == 0) return null;
                // Prefer exact input index match
                var exact = candidates.FirstOrDefault(d => d.Id.InputIndex == inputIndex);
                if (exact != null) return exact;
                // Log if no exact input index match among candidates
                try
                {
                    var sb2 = new System.Text.StringBuilder();
                    sb2.AppendLine(DateTime.Now.ToString("o") + " InputMapService.TryFindDownTrackInput: no exact input match among candidates for device='" + device + "' inputIndex=" + inputIndex);
                    sb2.AppendLine("  Candidates:\n" + string.Join("\n", candidates.Select(c => $"    {c.Id.Device}:{c.Id.InputIndex}")));
                    System.IO.File.AppendAllText("/tmp/pulsar_mqtt.log", sb2.ToString());
                }
                catch { }
                return null;
            }
            finally { _lock.ExitReadLock(); }
        }

        public void Dispose()
        {
            try { _watcher?.Dispose(); } catch { }
            _lock.Dispose();
        }
    }
}
