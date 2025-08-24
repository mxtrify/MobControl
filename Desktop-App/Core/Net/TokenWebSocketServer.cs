using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MobControlUI.Core.Logging;

namespace MobControlUI.Core.Net
{
    /// <summary>
    /// WebSocket server with:
    /// - Adapter filtering (ignores virtual/loopback/tunnel/WSL/NPCap/etc.)
    /// - Port fail-safe (tries sequential ports if requested one is busy)
    /// - Rich status reporting with reason codes + details and an OnStatusChanged event
    /// </summary>
    public sealed class TokenWebSocketServer : IDisposable
    {
        // ----- Status model -----
        public enum ServerStatusReason
        {
            Stopped = 0,
            Starting,
            Listening,
            PortConflict,          // no port available in the retry window
            NoEligibleAdapters,    // only localhost bound (or none)
            AclDenied,             // Windows URLACL not reserved / access denied
            NetworkUnavailable,    // NIC down or stack not ready
            UnknownError
        }

        public ServerStatusReason StatusReason { get; private set; } = ServerStatusReason.Stopped;
        public string? StatusDetail { get; private set; }
        public int ActualPort { get; private set; } // final chosen port after fail-safe
        public IReadOnlyList<string> BoundPrefixes => _boundPrefixes.AsReadOnly();
        public event Action<ServerStatusReason, string?>? OnStatusChanged;

        private void SetStatus(ServerStatusReason reason, string? detail = null)
        {
            StatusReason = reason;
            StatusDetail = detail;
            OnStatusChanged?.Invoke(reason, detail);
        }

        // ----- Internal state -----
        private sealed class DeviceState
        {
            public Guid Id { get; } = Guid.NewGuid();
            public string Token { get; init; } = "";
            public string? DeviceName { get; set; }
            public string? LayoutTitle { get; set; }
            public string[] Actions { get; set; } = Array.Empty<string>();
            public WebSocket Socket { get; init; } = default!;
        }

        private CancellationTokenSource? _cts;
        private readonly HttpListener _listener = new();
        private readonly ISessionManager _sessions;
        private readonly ILogService _log;

        private readonly Dictionary<string, HashSet<WebSocket>> _clients = new();
        private readonly Dictionary<WebSocket, DeviceState> _bySocket = new();
        private readonly Dictionary<Guid, DeviceState> _byId = new();
        private readonly HashSet<string> _singleUseReservations = new();
        private readonly object _gate = new();

        private readonly List<string> _boundPrefixes = new();
        private bool _prefixPrepared;

        // optional token-level events
        public event Action<Guid, string /*token*/, string /*deviceName*/>? OnDeviceIdentified;
        public event Action<Guid /*id*/, string /*layoutTitle*/, string[] /*actions*/>? OnLayoutDeclared;
        public event Action<Guid /*id*/, string /*deviceName*/>? OnDeviceDisconnected;
        public event Action<string /*token*/>? OnFirstClientForToken;
        public event Action<string /*token*/>? OnTokenBecameEmpty;
        public event Action<string /*token*/, int /*count*/>? OnClientCountChanged;
        public event Action<Guid /*deviceId*/, string /*token*/, string /*raw*/>? OnRawMessage;

        public record DeviceInfo(Guid Id, string Token, string? DeviceName, string? LayoutTitle, string[] Actions);

        public IReadOnlyList<DeviceInfo> GetDevices()
        {
            lock (_gate)
                return _bySocket.Values
                    .Select(s => new DeviceInfo(s.Id, s.Token, s.DeviceName, s.LayoutTitle, s.Actions.ToArray()))
                    .ToList();
        }

        public bool Disconnect(Guid id)
        {
            lock (_gate)
            {
                if (!_byId.TryGetValue(id, out var st)) return false;
                try { st.Socket.Abort(); } catch { }
                return true;
            }
        }

        // ---------- Constructor: ignores virtual adapters + port fail-safe + prepares prefixes ----------
        public TokenWebSocketServer(ISessionManager sessions, ILogService log)
        {
            _sessions = sessions;
            _log = log;

            PreparePrefixesWithFailSafe();
        }

        private void PreparePrefixesWithFailSafe()
        {
            SetStatus(ServerStatusReason.Starting, "Preparing listener prefixes…");

            var requestedPort = _sessions.Port;             // read-only in your interface
            var host = (_sessions.HostAddress ?? "").Trim();

            const int maxRetries = 20;
            int retries = 0;
            int port = requestedPort;
            bool prepared = false;

            while (!prepared && retries < maxRetries)
            {
                try
                {
                    _listener.Prefixes.Clear();
                    _boundPrefixes.Clear();

                    // Always include localhost
                    var pLocal = $"http://localhost:{port}/ws/";
                    _listener.Prefixes.Add(pLocal);
                    _boundPrefixes.Add(pLocal);

                    // If a concrete host IP was supplied and is eligible, bind to that too
                    if (IsConcreteIp(host) &&
                        IPAddress.TryParse(host, out var explicitIp) &&
                        IsEligibleLanIPv4(explicitIp))
                    {
                        var p = $"http://{explicitIp}:{port}/ws/";
                        _listener.Prefixes.Add(p);
                        _boundPrefixes.Add(p);
                        _log.Add($"WS: Using explicit host {explicitIp}:{port} (eligible LAN adapter)");
                    }
                    else
                    {
                        // Log a NIC survey so you can see gateway presence and which adapter got filtered.
                        LogNicSurvey();

                        // Prefer Wi‑Fi with a default gateway, then Ethernet+GW, then others.
                        var addrs = GetEligibleLanIPv4sPreferWifi().ToList();

                        if (addrs.Count == 0)
                        {
                            // We'll still operate on localhost; signal that to the UI later.
                            _log.Add("WS: WARNING – No eligible LAN adapters found. Binding only to localhost.");
                        }
                        else
                        {
                            foreach (var ip in addrs)
                            {
                                var p = $"http://{ip}:{port}/ws/";
                                _listener.Prefixes.Add(p);
                                _boundPrefixes.Add(p);
                            }
                            _log.Add($"WS: Eligible LAN bindings (preferred tier): {string.Join(", ", addrs.Select(a => $"{a}:{port}"))}");
                        }

                        if (IsConcreteIp(host) && IPAddress.TryParse(host, out explicitIp) && !IsEligibleLanIPv4(explicitIp))
                        {
                            _log.Add($"WS: WARNING – Provided HostAddress '{host}' is not an eligible LAN IPv4 (likely virtual/tunnel/APIPA).");
                        }
                    }

                    // Probe start/stop to confirm the port can be used
                    _listener.Start();
                    _listener.Stop();

                    prepared = true;
                    ActualPort = port;

                    if (port != requestedPort)
                        _log.Add($"WS: Port fail-safe engaged. Requested {requestedPort} → using {port}.");
                }
                catch (HttpListenerException)
                {
                    // Port likely in use → try next
                    retries++;
                    port++;
                }
            }

            if (!prepared)
            {
                ActualPort = requestedPort; // best-effort
                _prefixPrepared = false;
                SetStatus(ServerStatusReason.PortConflict, $"No free port in range {requestedPort}..{requestedPort + maxRetries - 1}");
                return;
            }

            _prefixPrepared = true;

            // If we only have localhost, surface a softer warning reason (still usable on same machine)
            var onlyLocalhost = _boundPrefixes.All(p => p.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase));
            if (onlyLocalhost)
                SetStatus(ServerStatusReason.NoEligibleAdapters, "Bound to localhost only (no eligible LAN adapters).");
            else
                SetStatus(ServerStatusReason.Starting, $"Prepared {string.Join(", ", _boundPrefixes)}");
        }
        // -----------------------------------------------------------------------------------------------

        public bool Start()
        {
            if (_cts != null) return true;

            if (!_prefixPrepared)
            {
                // Constructor couldn't prepare prefixes (e.g., port conflicts)
                return false;
            }

            _cts = new CancellationTokenSource();
            try
            {
                _listener.Start();

                foreach (var p in _boundPrefixes)
                    _log.Add($"WS: Listening at {p}");

                SetStatus(ServerStatusReason.Listening, string.Join(", ", _boundPrefixes));
                _ = AcceptLoopAsync(_cts.Token);
                return true;
            }
            catch (HttpListenerException hex)
            {
                // Common classification:
                // 5 → Access denied (URLACL not reserved) on Windows
                if (hex.ErrorCode == 5)
                {
                    SetStatus(ServerStatusReason.AclDenied,
                        "Access denied starting listener. On Windows, reserve the URLACL or run elevated.\n" +
                        "Example: netsh http add urlacl url=http://+:PORT/ws/ user=DOMAIN\\User");
                }
                else
                {
                    SetStatus(ServerStatusReason.UnknownError, $"HttpListenerException {hex.ErrorCode}: {hex.Message}");
                }

                _log.Add($"WS: Failed to start – {hex.Message}");
                Stop();
                return false;
            }
            catch (Exception ex)
            {
                // Network stack down or unexpected failure
                SetStatus(ServerStatusReason.UnknownError, ex.Message);
                _log.Add($"WS: Failed to start – {ex.Message}");
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }

            lock (_gate)
            {
                foreach (var set in _clients.Values)
                    foreach (var s in set.ToList())
                        try { s.Abort(); } catch { }
                _clients.Clear();
                _bySocket.Clear();
                _byId.Clear();
                _singleUseReservations.Clear();
            }

            _cts?.Dispose();
            _cts = null;

            SetStatus(ServerStatusReason.Stopped, "Listener stopped.");
            _log.Add("WS: Stopped.");
        }

        private static string Elide(string s, int max = 1024)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("\r", "\\r").Replace("\n", "\\n");
            return s.Length <= max ? s : s.Substring(0, max) + $"…(+{s.Length - max} chars)";
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch when (ct.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { if (ct.IsCancellationRequested) break; else continue; }
                catch (Exception ex) { _log.Add($"WS: Accept error – {ex.Message}"); continue; }

                _ = Task.Run(() => HandleContextAsync(ctx, ct), ct);
            }
        }

        private async Task HandleContextAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            var url = ctx.Request.Url;
            var path = url?.AbsolutePath?.TrimEnd('/');
            if (!ctx.Request.IsWebSocketRequest || url is null || !string.Equals(path, "/ws", StringComparison.Ordinal))
            {
                try { ctx.Response.StatusCode = 400; ctx.Response.Close(); } catch { }
                return;
            }

            var token = GetQueryParam(url, "token");
            if (string.IsNullOrWhiteSpace(token) || !_sessions.Validate(token!))
            {
                try { ctx.Response.StatusCode = 401; ctx.Response.Close(); } catch { }
                return;
            }

            // single-use / anti-race
            lock (_gate)
            {
                var hasClient = _clients.TryGetValue(token!, out var set) && set.Count > 0;
                if (hasClient || _singleUseReservations.Contains(token!))
                {
                    try { ctx.Response.StatusCode = 409; ctx.Response.Close(); } catch { }
                    return;
                }
                _singleUseReservations.Add(token!);
            }

            WebSocket socket;
            try
            {
                var wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
                socket = wsCtx.WebSocket;
            }
            catch (Exception ex)
            {
                _log.Add($"WS: Upgrade failed – {ex.Message}");
                lock (_gate) _singleUseReservations.Remove(token!);
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
                return;
            }

            _log.Add($"WS: Connected {ctx.Request.RemoteEndPoint} token={token}");

            DeviceState state;
            lock (_gate)
            {
                _singleUseReservations.Remove(token!);

                if (!_clients.TryGetValue(token!, out var set))
                    _clients[token!] = set = new HashSet<WebSocket>();
                set.Add(socket);

                state = new DeviceState { Token = token!, Socket = socket };
                _bySocket[socket] = state;
                _byId[state.Id] = state;

                var count = set.Count;
                OnClientCountChanged?.Invoke(token!, count);
                if (count == 1) OnFirstClientForToken?.Invoke(token!);
            }

            // first frame = device name (or plain text if not JSON)
            var deviceName = await ReadDeviceNameAsync(socket, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                lock (_gate) state.DeviceName = deviceName!;
                _log.Add($"WS: Device '{deviceName}' identified (id={state.Id}) token={token}");
                OnDeviceIdentified?.Invoke(state.Id, token!, deviceName!);
            }
            else
            {
                _log.Add($"WS: No device name provided (id={state.Id}) token={token}");
            }

            try
            {
                var buffer = new byte[8 * 1024];
                while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    var text = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    _log.Add($"WS RX id={state.Id} token={token} raw: {Elide(text)}", "Debug");

                    // layout payload?
                    if (TryParseLayout(text, out var title, out var actions))
                    {
                        lock (_gate) { state.LayoutTitle = title; state.Actions = actions; }
                        _log.Add($"WS: Layout '{title}' for id={state.Id} ({actions.Length} actions)");
                        OnLayoutDeclared?.Invoke(state.Id, title, actions);
                        continue;
                    }

                    // hand raw frames to input pipeline
                    OnRawMessage?.Invoke(state.Id, token!, text);

                    // optional broadcast to same-token peers
                    await BroadcastAsync(token!, text, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _log.Add($"WS: Error – {ex.Message}");
            }
            finally
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false); } catch { }

                string? devName;
                lock (_gate)
                {
                    devName = state.DeviceName;

                    _bySocket.Remove(socket);
                    _byId.Remove(state.Id);

                    if (_clients.TryGetValue(token!, out var set))
                    {
                        set.Remove(socket);
                        var count = set.Count;
                        OnClientCountChanged?.Invoke(token!, count);
                        if (count == 0) { _clients.Remove(token!); OnTokenBecameEmpty?.Invoke(token!); }
                    }
                }

                OnDeviceDisconnected?.Invoke(state.Id, devName ?? "(device)");
                _log.Add($"WS: Disconnected id={state.Id} token={token}");
            }
        }

        private Task BroadcastAsync(string token, string message, CancellationToken ct)
        {
            WebSocket[] targets;
            lock (_gate)
                targets = _clients.TryGetValue(token, out var set) ? set.ToArray() : Array.Empty<WebSocket>();

            if (targets.Length == 0) return Task.CompletedTask;

            var bytes = Encoding.UTF8.GetBytes(message);
            var seg = new ArraySegment<byte>(bytes);

            return Task.WhenAll(targets.Select(s =>
                s.State == WebSocketState.Open
                    ? s.SendAsync(seg, WebSocketMessageType.Text, endOfMessage: true, cancellationToken: ct)
                    : Task.CompletedTask));
        }

        private async Task<string?> ReadDeviceNameAsync(WebSocket socket, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                var buffer = new byte[4096];
                var result = await socket.ReceiveAsync(buffer, cts.Token).ConfigureAwait(false);
                if (result.MessageType != WebSocketMessageType.Text) return null;

                var text = Encoding.UTF8.GetString(buffer, 0, result.Count).Trim();
                _log.Add($"WS RX[first] raw: {Elide(text)}", "Debug");

                if (string.IsNullOrEmpty(text)) return null;

                try
                {
                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("deviceName", out var dn) && dn.ValueKind == JsonValueKind.String)
                            return dn.GetString();
                        if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                            return n.GetString();
                    }
                }
                catch { /* not JSON */ }

                return text;
            }
            catch (OperationCanceledException) { return null; }
            catch { return null; }
        }

        private static bool TryParseLayout(string text, out string title, out string[] actions)
        {
            title = "";
            actions = Array.Empty<string>();
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;

                if (root.TryGetProperty("type", out var t) &&
                    string.Equals(t.GetString(), "layout", StringComparison.OrdinalIgnoreCase) &&
                    root.TryGetProperty("title", out var ttl) && ttl.ValueKind == JsonValueKind.String &&
                    root.TryGetProperty("actions", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    var list = arr.EnumerateArray()
                                  .Where(e => e.ValueKind == JsonValueKind.String)
                                  .Select(e => e.GetString()!)
                                  .Where(s => !string.IsNullOrWhiteSpace(s))
                                  .ToArray();
                    if (list.Length > 0)
                    {
                        title = ttl.GetString()!;
                        actions = list;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static string? GetQueryParam(Uri url, string key)
        {
            var q = url.Query;
            if (string.IsNullOrEmpty(q)) return null;
            if (q[0] == '?') q = q.Substring(1);

            foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                var k = eq >= 0 ? part[..eq] : part;
                if (!string.Equals(k, key, StringComparison.Ordinal)) continue;
                return eq >= 0 ? Uri.UnescapeDataString(part[(eq + 1)..]) : "";
            }
            return null;
        }

        public void Dispose() => Stop();

        // ========================= NIC / IP helpers =========================

        private static readonly string[] _virtualKeywords = new[]
        {
            "virtual", "vmware", "hyper-v", "vethernet", "vbox", "virtualbox", "loopback",
            "tap", "tunnel", "teredo", "isatap", "npcap", "nmap", "container", "wsl"
        };

        private static bool IsConcreteIp(string host) =>
            !string.IsNullOrWhiteSpace(host) &&
            host != "*" && host != "+" && host != "0.0.0.0" && host.ToLowerInvariant() != "auto";

        private static bool IsEligibleLanIPv4(IPAddress addr)
        {
            if (addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false; // IPv4 only
            if (IPAddress.IsLoopback(addr)) return false;

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;

                var ipProps = ni.GetIPProperties();
                var hasThisIp = ipProps.UnicastAddresses.Any(u => u.Address.Equals(addr));
                if (!hasThisIp) continue;

                if (IsVirtualOrUnsupportedNic(ni)) return false;
                return true; // found owning NIC and it's eligible
            }
            return false;
        }

        private static IEnumerable<IPAddress> GetEligibleLanIPv4s()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (IsVirtualOrUnsupportedNic(ni)) continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    var ip = ua.Address;
                    if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue; // IPv4 only
                    if (IPAddress.IsLoopback(ip)) continue;

                    yield return ip;
                }
            }
        }

        // --------- Wi‑Fi‑first prioritization helpers ---------

        private static bool HasDefaultGateway(NetworkInterface ni)
        {
            try
            {
                var gws = ni.GetIPProperties()?.GatewayAddresses;
                if (gws == null) return false;
                return gws.Any(g =>
                    g?.Address != null &&
                    g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !IPAddress.Any.Equals(g.Address) &&
                    !IPAddress.Loopback.Equals(g.Address));
            }
            catch { return false; }
        }

        private static bool IsApipa(IPAddress ip)
        {
            var b = ip.GetAddressBytes();
            return b.Length == 4 && b[0] == 169 && b[1] == 254;
        }

        /// <summary>
        /// Enumerates eligible IPv4s with a tier:
        /// Tier 1: Wireless80211 + default gateway
        /// Tier 2: Ethernet + default gateway
        /// Tier 3: other eligible (no gateway etc.)
        /// APIPA is excluded. Virtual/loopback/tunnel are excluded.
        /// </summary>
        private static IEnumerable<(IPAddress ip, NetworkInterface ni, int tier)> EnumerateEligibleLanIPv4sWithTier()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (IsVirtualOrUnsupportedNic(ni)) continue;

                var hasGw = HasDefaultGateway(ni);
                int tier =
                    ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && hasGw ? 1 :
                    (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet && hasGw ? 2 : 3);

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    var ip = ua.Address;
                    if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(ip)) continue;
                    if (IsApipa(ip)) continue;

                    yield return (ip, ni, tier);
                }
            }
        }

        /// <summary>
        /// Prefer NICs that have a default gateway. If any NICs with a gateway exist,
        /// IGNORE all NICs without a gateway (e.g., host-only 192.168.56.1).
        /// Tier 1: Wireless80211 + gateway
        /// Tier 2: Ethernet + gateway
        /// Fallback (only if no NIC has a gateway):
        /// Tier 3: other eligible NICs (no gateway) — ordered by NIC speed.
        /// </summary>
        private static IEnumerable<IPAddress> GetEligibleLanIPv4sPreferWifi()
        {
            var all = EnumerateEligibleLanIPv4sWithTier().ToList();
            if (all.Count == 0) yield break;

            // Split by "has gateway" vs "no gateway"
            var withGw = all.Where(x => HasDefaultGateway(x.ni)).ToList();
            var noGw = all.Where(x => !HasDefaultGateway(x.ni)).ToList();

            IEnumerable<(IPAddress ip, NetworkInterface ni, int tier)> chosen;

            if (withGw.Count > 0)
            {
                // Among NICs with a gateway, choose the best tier (1 = Wi-Fi, 2 = Ethernet)
                var bestTier = withGw.Min(t => t.tier);
                chosen = withGw.Where(t => t.tier == bestTier);
            }
            else
            {
                // No NIC reports a gateway (airgapped / captive portal / ICS / etc.)
                // Fall back to prior behaviour but ONLY from the "no gateway" list.
                var bestTier = noGw.Min(t => t.tier);
                chosen = noGw.Where(t => t.tier == bestTier);
            }

            foreach (var item in chosen.OrderByDescending(t => t.ni.Speed))
                yield return item.ip;
        }

        private static bool IsVirtualOrUnsupportedNic(NetworkInterface ni)
        {
            switch (ni.NetworkInterfaceType)
            {
                case NetworkInterfaceType.Loopback:
                case NetworkInterfaceType.Tunnel:
                case NetworkInterfaceType.Unknown:
                case NetworkInterfaceType.Ppp:
                    return true;
            }

            var name = (ni.Name ?? "").ToLowerInvariant();
            var desc = (ni.Description ?? "").ToLowerInvariant();

            // Stronger keyword filter
            string[] blockKeywords =
            {
                "virtual", "vmware", "hyper-v", "vethernet", "vbox", "virtualbox",
                "host-only", "host only", "loopback", "tap", "tunnel", "teredo",
                "isatap", "npcap", "nmap", "container", "wsl"
            };

            if (blockKeywords.Any(k => name.Contains(k) || desc.Contains(k)))
                return true;

            return false; // otherwise keep
        }

        private void LogNicSurvey()
        {
            try
            {
                _log.Add("WS: ---- NIC Survey ----");
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces().OrderByDescending(n => n.OperationalStatus == OperationalStatus.Up))
                {
                    var up = ni.OperationalStatus == OperationalStatus.Up;
                    var type = ni.NetworkInterfaceType;
                    var speedMbps = ni.Speed > 0 ? (ni.Speed / 1_000_000) : 0;
                    var isBlocked = IsVirtualOrUnsupportedNic(ni);
                    var hasGw = HasDefaultGateway(ni);
                    var ips = ni.GetIPProperties().UnicastAddresses
                        .Select(u => u.Address)
                        .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                        .Select(a => a.ToString())
                        .ToArray();

                    _log.Add(
                        $"WS: NIC '{ni.Name}' ({ni.Description}) | Up={up} Type={type} Speed={speedMbps}Mbps " +
                        $"Blocked={isBlocked} HasGW={hasGw} IPs=[{string.Join(", ", ips)}]"
                    );
                }
                _log.Add("WS: ---- End NIC Survey ----");
            }
            catch (Exception ex)
            {
                _log.Add($"WS: NIC survey failed: {ex.Message}");
            }
        }


    }
}