using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MobControlUI.Core.Mapping;

namespace MobControlUI.Core.Storage
{
    public sealed class InputMappingStore : IInputMappingStore
    {
        private static string FileOf(string name)
        {
            var safe = AppPaths.SanitizeFileName(name);
            return Path.Combine(AppPaths.MappingsDir, safe + ".json");
        }

        public Task<IReadOnlyList<string>> ListAsync()
        {
            AppPaths.EnsureCreated();

            var files = Directory.EnumerateFiles(AppPaths.MappingsDir, "*.json")
                .Select(p => Path.GetFileNameWithoutExtension(p))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Task.FromResult<IReadOnlyList<string>>(files);
        }

        public Task<bool> ExistsAsync(string name)
        {
            var path = FileOf(name);
            return Task.FromResult(File.Exists(path));
        }

        // -----------------------------
        // SAVE
        // -----------------------------

        public async Task SaveAsync<T>(string name, T mapping)
        {
            AppPaths.EnsureCreated();

            var path = FileOf(name);
            var tmp = path + ".tmp";

            var json = JsonSerializer.Serialize(mapping, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);

            // Atomic replace to avoid partial writes
            if (File.Exists(path))
                File.Replace(tmp, path, destinationBackupFileName: null);
            else
                File.Move(tmp, path);
        }

        // Convenience overload for the unified on-disk schema
        public Task SaveAsync(string name, InputMappingFile file) => SaveAsync<InputMappingFile>(name, file);

        // -----------------------------
        // LOAD
        // -----------------------------

        public async Task<T?> LoadAsync<T>(string name)
        {
            var path = FileOf(name);
            if (!File.Exists(path)) return default;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<T>(fs, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Flexible loader that accepts both:
        /// 1) New format: { "version":1, "layout":"...", "bindings": { action: hotkey, ... } }
        /// 2) Legacy flat format: { action: hotkey, ... }
        /// Returns a unified <see cref="InputMappingFile"/>.
        /// </summary>
        public async Task<InputMappingFile?> LoadFlexibleAsync(string name)
        {
            var path = FileOf(name);
            if (!File.Exists(path)) return null;

            // Try preferred schema first
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var file = await JsonSerializer.DeserializeAsync<InputMappingFile>(
                    fs,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    .ConfigureAwait(false);

                if (file is not null && file.Bindings is not null && file.Bindings.Count >= 0)
                    return file;
            }
            catch
            {
                // fall back to legacy handling below
            }

            // Fallback: attempt to parse JSON and detect legacy shape
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            var node = JsonNode.Parse(json) as JsonObject;
            if (node is null) return null;

            // Legacy: direct action->key pairs (all primitive values)
            bool looksFlat = node.All(kvp => kvp.Value is JsonValue);
            if (looksFlat)
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in node)
                {
                    if (kvp.Value is JsonValue v && v.TryGetValue<string>(out var s))
                        map[kvp.Key] = s;
                }

                return new InputMappingFile
                {
                    Version = 1,
                    Layout = "", // unknown in legacy files
                    Bindings = map
                };
            }

            // Otherwise, try to manually pick out fields (tolerant to casing)
            var bindingsObj = (node["bindings"] ?? node["Bindings"]) as JsonObject;
            var layout = (string?)(node["layout"] ?? node["Layout"]) ?? "";

            var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (bindingsObj is not null)
            {
                foreach (var kv in bindingsObj)
                {
                    if (kv.Value is JsonValue v && v.TryGetValue<string>(out var s))
                        bindings[kv.Key] = s;
                }
            }

            return new InputMappingFile
            {
                Version = 1,
                Layout = layout,
                Bindings = bindings
            };
        }

        // -----------------------------
        // DELETE / RENAME / SEED
        // -----------------------------

        public Task DeleteAsync(string name)
        {
            var path = FileOf(name);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }

        public Task RenameAsync(string oldName, string newName)
        {
            var src = FileOf(oldName);
            var dst = FileOf(newName);
            if (!File.Exists(src)) throw new FileNotFoundException($"Mapping not found: {oldName}", src);
            if (File.Exists(dst)) throw new IOException($"Target name already exists: {newName}");
            File.Move(src, dst);
            return Task.CompletedTask;
        }

        public Task SeedDefaultsAsync(bool overwrite = false)
        {
            AppPaths.EnsureCreated();

            // If not overwriting and we already have files, do nothing
            var hasAny = Directory.EnumerateFiles(AppPaths.MappingsDir, "*.json").Any();
            if (!overwrite && hasAny) return Task.CompletedTask;

            if (!Directory.Exists(AppPaths.DefaultsDir)) return Task.CompletedTask;

            foreach (var src in Directory.EnumerateFiles(AppPaths.DefaultsDir, "*.json"))
            {
                var name = Path.GetFileName(src); // includes .json
                var dst = Path.Combine(AppPaths.MappingsDir, name);

                if (overwrite || !File.Exists(dst))
                {
                    Directory.CreateDirectory(AppPaths.MappingsDir);
                    File.Copy(src, dst, overwrite);
                }
            }

            return Task.CompletedTask;
        }
    }
}