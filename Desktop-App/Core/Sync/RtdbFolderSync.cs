using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MobControlUI.Core.Logging;

namespace MobControlUI.Core.Sync
{
    public sealed class RtdbFolderPair
    {
        public RtdbFolderPair(string localDir, string remoteNode)
        {
            LocalDir = localDir;
            RemoteNode = remoteNode.Trim('/'); // e.g. "mappings" or "associations"
        }

        /// <summary>Local folder to sync.</summary>
        public string LocalDir { get; }

        /// <summary>Remote node name under the RTDB root.</summary>
        public string RemoteNode { get; }

        /// <summary>
        /// If set, only these file names (within LocalDir) are synced for this pair.
        /// Use for single-file folders (e.g., "LayoutMappingAssociations.json").
        /// </summary>
        public List<string>? IncludeFiles { get; init; }
    }

    public sealed class RtdbSyncOptions
    {
        /// <summary>Base RTDB URL without trailing slash. e.g. "https://...firebasedatabase.app"</summary>
        public string BaseUrl { get; init; } = "";

        /// <summary>Optional auth query param (?auth=...). Use ID token or database secret. Leave null/empty to omit.</summary>
        public string? AuthToken { get; init; }

        /// <summary>Pairs of local folder ↔ remote node to sync.</summary>
        public List<RtdbFolderPair> Pairs { get; } = new();

        /// <summary>If true, newer side wins on conflicts (applies to both download & upload).</summary>
        public bool UseLastWriteWins { get; init; } = true;

        /// <summary>If true, delete remote entries that no longer exist locally.</summary>
        public bool MirrorDeletes { get; init; } = true;
    }

    public interface IFirebaseRtdbFolderSync
    {
        /// <summary>Remote → Local</summary>
        Task DownloadAllAsync(CancellationToken ct = default);

        /// <summary>Local → Remote (await responses, logs results)</summary>
        Task UploadAllAsync(CancellationToken ct = default);

        /// <summary>
        /// Local → Remote best-effort for shutdown: fire PUT/DELETEs, wait up to overallBudget, then return.
        /// Does not await bodies; uses ResponseHeadersRead & print=silent.
        /// </summary>
        Task UploadAllBestEffortAsync(TimeSpan overallBudget, CancellationToken ct = default);
    }

    internal sealed class RtdbEntry
    {
        public string? file_name { get; set; }     // Original filename (e.g., "LayoutMappingAssociations.json")
        public string? content_b64 { get; set; }
        public string? content { get; set; }       // fallback
        public string? modified { get; set; }      // ISO-8601 UTC
        public string? content_type { get; set; }  // e.g., application/json
    }

    public sealed class FirebaseRtdbFolderSync : IFirebaseRtdbFolderSync
    {
        private readonly HttpClient _http;
        private readonly RtdbSyncOptions _opt;
        private readonly ILogService _log;

        public FirebaseRtdbFolderSync(HttpClient http, RtdbSyncOptions opt, ILogService log)
        {
            _http = http;
            _opt = opt;
            _log = log;
        }

        // -------------------- Download (remote -> local) --------------------

        public async Task DownloadAllAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_opt.BaseUrl))
            {
                _log.Add("Sync: RTDB BaseUrl not configured. Skipping download.", "Warn");
                return;
            }

            foreach (var pair in _opt.Pairs)
            {
                Directory.CreateDirectory(pair.LocalDir);
                _log.Add($"Sync: ↓ {_opt.BaseUrl}/{pair.RemoteNode} → {pair.LocalDir}");

                HashSet<string>? allow = null;
                if (pair.IncludeFiles is { Count: > 0 })
                    allow = new HashSet<string>(pair.IncludeFiles, StringComparer.OrdinalIgnoreCase);

                var url = BuildUrl($"{pair.RemoteNode}.json");
                using var resp = await _http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _log.Add($"Sync: GET {url} → {(int)resp.StatusCode}. Skipping node.", "Warn");
                    continue;
                }

                using var s = await resp.Content.ReadAsStreamAsync(ct);
                var dict = await JsonSerializer.DeserializeAsync<Dictionary<string, RtdbEntry>>(s, cancellationToken: ct)
                            ?? new Dictionary<string, RtdbEntry>();

                foreach (var kv in dict)
                {
                    var entry = kv.Value;
                    if (entry is null) continue;

                    var fileName = string.IsNullOrWhiteSpace(entry.file_name) ? kv.Key : entry.file_name;
                    if (string.IsNullOrWhiteSpace(fileName)) continue;

                    if (allow is not null && !allow.Contains(fileName)) continue;

                    var localPath = Path.Combine(pair.LocalDir, fileName);
                    var remoteUtc = ParseUtc(entry.modified) ?? DateTime.MinValue;
                    var needWrite = !File.Exists(localPath);

                    if (!needWrite && _opt.UseLastWriteWins)
                    {
                        var localUtc = File.GetLastWriteTimeUtc(localPath);
                        needWrite = remoteUtc > localUtc;
                    }
                    if (!needWrite) continue;

                    var bytes = Decode(entry);
                    if (bytes is null) continue;

                    await File.WriteAllBytesAsync(localPath, bytes, ct);
                    if (remoteUtc > DateTime.MinValue)
                        File.SetLastWriteTimeUtc(localPath, remoteUtc);

                    _log.Add($"Sync: downloaded {fileName}");
                }
            }

            _log.Add("Sync: Download complete.");
        }

        // -------------------- Upload (local -> remote, strict) --------------------

        public async Task UploadAllAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_opt.BaseUrl))
            {
                _log.Add("Sync: RTDB BaseUrl not configured. Skipping upload.", "Warn");
                return;
            }

            foreach (var pair in _opt.Pairs)
            {
                if (!Directory.Exists(pair.LocalDir))
                {
                    _log.Add($"Sync: Local folder missing → {pair.LocalDir}. Skipping.", "Warn");
                    continue;
                }

                _log.Add($"Sync: ↑ {pair.LocalDir} → {_opt.BaseUrl}/{pair.RemoteNode}");

                // Load remote index for LWW + deletes
                Dictionary<string, RtdbEntry> remoteMap = new();
                try
                {
                    var idxUrl = BuildUrl($"{pair.RemoteNode}.json");
                    using var r = await _http.GetAsync(idxUrl, ct);
                    if (r.IsSuccessStatusCode)
                    {
                        var st = await r.Content.ReadAsStreamAsync(ct);
                        remoteMap = await JsonSerializer.DeserializeAsync<Dictionary<string, RtdbEntry>>(st, cancellationToken: ct)
                                    ?? new Dictionary<string, RtdbEntry>();
                    }
                    else
                    {
                        var body = await r.Content.ReadAsStringAsync(ct);
                        _log.Add($"Sync: index GET failed → {(int)r.StatusCode} {r.ReasonPhrase}. Body: {Trunc(body)}", "Warn");
                    }
                }
                catch (Exception ex)
                {
                    _log.Add($"Sync: index GET exception: {ex.Message}", "Warn");
                }

                var remoteByOriginalName = new Dictionary<string, RtdbEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in remoteMap)
                {
                    var orig = kv.Value?.file_name ?? kv.Key;
                    if (!string.IsNullOrWhiteSpace(orig))
                        remoteByOriginalName[orig] = kv.Value!;
                }

                var localOriginalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var localSanitizedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Enumerate local files (respect IncludeFiles)
                IEnumerable<string> filesToSync =
                    pair.IncludeFiles is { Count: > 0 }
                    ? pair.IncludeFiles.Select(n => Path.Combine(pair.LocalDir, n)).Where(File.Exists)
                    : Directory.EnumerateFiles(pair.LocalDir, "*", SearchOption.TopDirectoryOnly);

                // Upserts
                foreach (var path in filesToSync)
                {
                    ct.ThrowIfCancellationRequested();

                    var name = Path.GetFileName(path);
                    localOriginalNames.Add(name);
                    localSanitizedNames.Add(SanitizeKey(name));

                    var localUtc = File.GetLastWriteTimeUtc(path);
                    remoteByOriginalName.TryGetValue(name, out var remote);
                    var remoteUtc = ParseUtc(remote?.modified);

                    var shouldUpload = remoteUtc == null || (!_opt.UseLastWriteWins || localUtc >= remoteUtc.Value);
                    if (!shouldUpload) continue;

                    var bytes = await File.ReadAllBytesAsync(path, ct);
                    var payload = new RtdbEntry
                    {
                        file_name = name,
                        content_b64 = Convert.ToBase64String(bytes),
                        content_type = GuessContentType(name),
                        modified = DateTime.UtcNow.ToString("o")
                    };

                    var safeKey = SanitizeKey(name);
                    var endpoint = BuildUrl($"{pair.RemoteNode}/{safeKey}.json", printSilent: true);
                    var json = JsonSerializer.Serialize(payload);

                    using var req = new HttpRequestMessage(HttpMethod.Put, endpoint)
                    { Content = new StringContent(json, Encoding.UTF8, "application/json") };

                    using var resp = await _http.SendAsync(req, ct);
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    if (resp.IsSuccessStatusCode)
                        _log.Add($"Sync: uploaded {name} → {endpoint}");
                    else
                        _log.Add($"Sync: upload failed for {name} → {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {Trunc(body)}. URL: {endpoint}", "Warn");
                }

                // Deletes (mirror)
                if (_opt.MirrorDeletes)
                {
                    foreach (var kv in remoteMap)
                    {
                        var remoteKey = kv.Key;
                        var entry = kv.Value;
                        var orig = entry?.file_name ?? remoteKey;

                        // If not present locally (by original name) AND not present as a sanitized name (legacy), delete
                        if (!localOriginalNames.Contains(orig) && !localSanitizedNames.Contains(remoteKey))
                        {
                            var delEndpoint = BuildUrl($"{pair.RemoteNode}/{remoteKey}.json", printSilent: true);
                            using var delResp = await _http.DeleteAsync(delEndpoint, ct);
                            var delBody = await delResp.Content.ReadAsStringAsync(ct);

                            if (delResp.IsSuccessStatusCode)
                                _log.Add($"Sync: deleted remote '{orig}' (key '{remoteKey}')");
                            else
                                _log.Add($"Sync: remote delete failed for '{orig}' (key '{remoteKey}') → {(int)delResp.StatusCode} {delResp.ReasonPhrase}. Body: {Trunc(delBody)}. URL: {delEndpoint}", "Warn");
                        }
                    }
                }
            }

            _log.Add("Sync: Upload complete.");
        }

        // -------------------- Upload (best-effort for shutdown) --------------------

        public async Task UploadAllBestEffortAsync(TimeSpan overallBudget, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_opt.BaseUrl)) return;

            var deadline = DateTime.UtcNow + overallBudget;
            var allTasks = new List<Task>();

            foreach (var pair in _opt.Pairs)
            {
                if (!Directory.Exists(pair.LocalDir)) continue;

                // Light index load; ignore failures in best-effort mode
                Dictionary<string, RtdbEntry> remoteMap = new();
                try
                {
                    var idxUrl = BuildUrl($"{pair.RemoteNode}.json");
                    using var r = await _http.GetAsync(idxUrl, ct);
                    if (r.IsSuccessStatusCode)
                    {
                        using var st = await r.Content.ReadAsStreamAsync(ct);
                        remoteMap = await JsonSerializer.DeserializeAsync<Dictionary<string, RtdbEntry>>(st, cancellationToken: ct)
                                    ?? new Dictionary<string, RtdbEntry>();
                    }
                }
                catch { /* ignore */ }

                var remoteByOriginal = new Dictionary<string, RtdbEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in remoteMap)
                {
                    var orig = kv.Value?.file_name ?? kv.Key;
                    if (!string.IsNullOrWhiteSpace(orig)) remoteByOriginal[orig] = kv.Value!;
                }

                var localOriginals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var localSanitized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Enumerate local files (respect IncludeFiles)
                IEnumerable<string> filesToSync =
                    pair.IncludeFiles is { Count: > 0 }
                    ? pair.IncludeFiles.Select(n => Path.Combine(pair.LocalDir, n)).Where(File.Exists)
                    : Directory.EnumerateFiles(pair.LocalDir, "*", SearchOption.TopDirectoryOnly);

                // Queue PUTs (no body wait)
                foreach (var path in filesToSync)
                {
                    if (DateTime.UtcNow >= deadline) break;

                    var name = Path.GetFileName(path);
                    localOriginals.Add(name);
                    localSanitized.Add(SanitizeKey(name));

                    if (_opt.UseLastWriteWins &&
                        remoteByOriginal.TryGetValue(name, out var rem) &&
                        ParseUtc(rem?.modified) is DateTime rutc &&
                        File.GetLastWriteTimeUtc(path) < rutc)
                    {
                        continue; // remote newer; skip
                    }

                    byte[] bytes;
                    try { bytes = File.ReadAllBytes(path); } catch { continue; }

                    var payload = new RtdbEntry
                    {
                        file_name = name,
                        content_b64 = Convert.ToBase64String(bytes),
                        content_type = GuessContentType(name),
                        modified = DateTime.UtcNow.ToString("o")
                    };

                    var safeKey = SanitizeKey(name);
                    var endpoint = BuildUrl($"{pair.RemoteNode}/{safeKey}.json", printSilent: true);
                    var json = JsonSerializer.Serialize(payload);
                    var req = new HttpRequestMessage(HttpMethod.Put, endpoint)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };

                    var remain = deadline - DateTime.UtcNow;
                    var perReqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    if (remain > TimeSpan.Zero) perReqCts.CancelAfter(remain);

                    allTasks.Add(
                        _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, perReqCts.Token)
                             .ContinueWith(_ => { /* ignore result */ }, TaskScheduler.Default));
                }

                // Queue DELETEs (mirror)
                if (_opt.MirrorDeletes && remoteMap.Count > 0)
                {
                    foreach (var kv in remoteMap)
                    {
                        if (DateTime.UtcNow >= deadline) break;

                        var remoteKey = kv.Key;
                        var orig = kv.Value?.file_name ?? remoteKey;

                        if (!localOriginals.Contains(orig) && !localSanitized.Contains(remoteKey))
                        {
                            var delEndpoint = BuildUrl($"{pair.RemoteNode}/{remoteKey}.json", printSilent: true);

                            var remain = deadline - DateTime.UtcNow;
                            var perReqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            if (remain > TimeSpan.Zero) perReqCts.CancelAfter(remain);

                            allTasks.Add(
                                _http.DeleteAsync(delEndpoint, perReqCts.Token)
                                     .ContinueWith(_ => { /* ignore result */ }, TaskScheduler.Default));
                        }
                    }
                }
            }

            // Cap total wait to overall budget
            var budgetCts = new CancellationTokenSource(overallBudget);
            try { await Task.WhenAny(Task.WhenAll(allTasks), Task.Delay(Timeout.InfiniteTimeSpan, budgetCts.Token)); }
            catch (OperationCanceledException) { /* budget elapsed */ }
        }

        // -------------------- Helpers --------------------

        /// <summary>Builds the full RTDB URL with optional auth and print=silent.</summary>
        private string BuildUrl(string pathWithJson, bool printSilent = false)
        {
            var baseUrl = _opt.BaseUrl.TrimEnd('/');

            var qs = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(_opt.AuthToken))
                qs.Add("auth=" + Uri.EscapeDataString(_opt.AuthToken!));
            if (printSilent) qs.Add("print=silent");

            var q = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
            return $"{baseUrl}/{pathWithJson}{q}";
        }

        private static byte[]? Decode(RtdbEntry e)
        {
            if (!string.IsNullOrEmpty(e.content_b64))
            {
                try { return Convert.FromBase64String(e.content_b64); } catch { return null; }
            }
            if (!string.IsNullOrEmpty(e.content))
            {
                try { return Encoding.UTF8.GetBytes(e.content); } catch { return null; }
            }
            return null;
        }

        private static DateTime? ParseUtc(string? iso)
        {
            if (string.IsNullOrWhiteSpace(iso)) return null;
            if (DateTime.TryParse(iso, null,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dt)) return dt.ToUniversalTime();
            return null;
        }

        /// <summary>RTDB disallows: '.', '#', '$', '/', '[', ']'</summary>
        private static string SanitizeKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "_";
            var sb = new StringBuilder(key.Length);
            foreach (var ch in key)
            {
                switch (ch)
                {
                    case '.':
                    case '#':
                    case '$':
                    case '/':
                    case '[':
                    case ']':
                        sb.Append('_'); break;
                    default:
                        sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        private static string GuessContentType(string fileName)
        {
            var ext = Path.GetExtension(fileName);
            if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase)) return "application/json";
            if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase)) return "text/plain";
            return "application/octet-stream";
        }

        private static string Trunc(string? s, int max = 500)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }
}