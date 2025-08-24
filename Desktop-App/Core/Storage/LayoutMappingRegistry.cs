using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MobControlUI.Core.Storage
{
    public interface ILayoutMappingRegistry
    {
        Task<HashSet<string>> GetMappingsAsync(string layoutTitle);
        Task<bool> IsAssociatedAsync(string layoutTitle, string mappingName);

        Task AssociateAsync(string layoutTitle, string mappingName);
        Task RemoveAsync(string layoutTitle, string mappingName);

        // Convenience alias used elsewhere
        Task AddAssociationAsync(string layoutTitle, string mappingName);

        // NEW: maintenance helpers used by Update/Delete flows
        Task RenameMappingAsync(string oldName, string newName);
        Task RemoveMappingEverywhereAsync(string mappingName);

        // Raised when any layout’s associations change (payload = layout title)
        event Action<string>? AssociationsChanged;
    }

    public sealed class LayoutMappingRegistry : ILayoutMappingRegistry
    {
        private readonly string _file = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MobControlUI", "LayoutMappingAssociations.json");

        private readonly SemaphoreSlim _gate = new(1, 1);

        // Case-insensitive layout titles and mapping names
        private Dictionary<string, HashSet<string>> _map =
            new(StringComparer.OrdinalIgnoreCase);

        public LayoutMappingRegistry()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            if (!File.Exists(_file)) TrySeedFromDefaults();
            Load();
            NormalizeAndSaveIfNeeded();
        }

        // ---------------- public API ----------------

        public async Task<HashSet<string>> GetMappingsAsync(string layoutTitle)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                return _map.TryGetValue(layoutTitle, out var set)
                    ? new HashSet<string>(set, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            finally { _gate.Release(); }
        }

        public async Task<bool> IsAssociatedAsync(string layoutTitle, string mappingName)
        {
            mappingName = NormalizeMappingName(mappingName);
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                return _map.TryGetValue(layoutTitle, out var set) && set.Contains(mappingName);
            }
            finally { _gate.Release(); }
        }

        public async Task AssociateAsync(string layoutTitle, string mappingName)
        {
            mappingName = NormalizeMappingName(mappingName);

            bool changed = false;
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_map.TryGetValue(layoutTitle, out var set))
                    _map[layoutTitle] = set = new(StringComparer.OrdinalIgnoreCase);

                if (set.Add(mappingName))
                {
                    Save();
                    changed = true;
                }
            }
            finally { _gate.Release(); }

            if (changed) AssociationsChanged?.Invoke(layoutTitle);
        }

        public async Task RemoveAsync(string layoutTitle, string mappingName)
        {
            mappingName = NormalizeMappingName(mappingName);

            bool changed = false;
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_map.TryGetValue(layoutTitle, out var set) && set.Remove(mappingName))
                {
                    Save();
                    changed = true;
                }
            }
            finally { _gate.Release(); }

            if (changed) AssociationsChanged?.Invoke(layoutTitle);
        }

        public Task AddAssociationAsync(string layoutTitle, string mappingName)
            => AssociateAsync(layoutTitle, mappingName);

        // ---- NEW: rename one mapping across all layouts
        public async Task RenameMappingAsync(string oldName, string newName)
        {
            oldName = NormalizeMappingName(oldName);
            newName = NormalizeMappingName(newName);

            var affected = new List<string>();

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                bool anyChange = false;

                foreach (var kv in _map)
                {
                    if (kv.Value.Remove(oldName))
                    {
                        kv.Value.Add(newName);
                        affected.Add(kv.Key);
                        anyChange = true;
                    }
                }

                if (anyChange) Save();
            }
            finally { _gate.Release(); }

            // notify per layout, outside the lock
            foreach (var layout in affected.Distinct(StringComparer.OrdinalIgnoreCase))
                AssociationsChanged?.Invoke(layout);
        }

        // ---- NEW: remove a mapping from all layouts
        public async Task RemoveMappingEverywhereAsync(string mappingName)
        {
            mappingName = NormalizeMappingName(mappingName);

            var affected = new List<string>();

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                bool anyChange = false;

                foreach (var kv in _map)
                {
                    if (kv.Value.Remove(mappingName))
                    {
                        affected.Add(kv.Key);
                        anyChange = true;
                    }
                }

                if (anyChange) Save();
            }
            finally { _gate.Release(); }

            foreach (var layout in affected.Distinct(StringComparer.OrdinalIgnoreCase))
                AssociationsChanged?.Invoke(layout);
        }

        public event Action<string>? AssociationsChanged;

        // ---------------- internals ----------------

        private void Load()
        {
            if (!File.Exists(_file))
            {
                _map = new(StringComparer.OrdinalIgnoreCase);
                return;
            }

            var json = File.ReadAllText(_file);
            var raw = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json)
                      ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            _map = raw.ToDictionary(
                kv => kv.Key,
                kv => new HashSet<string>(kv.Value ?? new(), StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        }

        private void Save()
        {
            var raw = _map.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

            var json = JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_file, json);
        }

        private static string NormalizeMappingName(string name)
        {
            var n = (name ?? string.Empty).Trim();
            if (n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                n = Path.GetFileNameWithoutExtension(n) ?? n;
            return n;
        }

        private void NormalizeAndSaveIfNeeded()
        {
            bool changed = false;

            foreach (var key in _map.Keys.ToList())
            {
                var set = _map[key];
                var normalized = new HashSet<string>(
                    set.Select(NormalizeMappingName),
                    StringComparer.OrdinalIgnoreCase);

                if (!set.SetEquals(normalized))
                {
                    _map[key] = normalized;
                    changed = true;
                }
            }

            if (changed) Save();
        }

        private void TrySeedFromDefaults()
        {
            try
            {
                var src = Path.Combine(AppContext.BaseDirectory, "Storage", "Defaults", "LayoutMappingAssociations.json");
                if (File.Exists(src))
                {
                    File.Copy(src, _file, overwrite: false);
                }
                else
                {
                    var defaults = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "DefaultLayout1", new List<string> { "DefaultMapping1" } },
                        { "DefaultLayout2", new List<string> { "DefaultMapping2" } }
                    };
                    var json = JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_file, json);
                }
            }
            catch
            {
                // best-effort seeding; ignore failures
            }
        }
    }
}