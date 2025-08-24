using MobControlUI.Core.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MobControlUI.Core.Net
{
    public sealed class SessionManager : ISessionManager
    {
        private readonly ILogService _log;
        private readonly ConcurrentDictionary<string, object?> _sessions = new();

        public int Port { get; set; }
        public string HostAddress { get; }   // kept for backwards compatibility / logging

        public SessionManager(ILogService log, int port = 8181)
        {
            _log = log;
            Port = port;
            HostAddress = GetLocalIPv4() ?? "127.0.0.1";
            _log.Add($"SessionManager: Initial Host guess={HostAddress}, Port={Port}");
        }

        public string CreateSession()
        {
            string token;
            do { token = CreateToken(); } while (!_sessions.TryAdd(token, null));
            _log.Add($"SessionManager: Created token {token}");
            return token;
        }

        public bool Validate(string token) => _sessions.ContainsKey(token);

        public void CloseSession(string token)
        {
            if (_sessions.TryRemove(token, out _))
                _log.Add($"SessionManager: Closed token {token}");
        }

        /// <summary>
        /// Builds a URL using the *actual* host and port bound by the WebSocket server,
        /// not the cached HostAddress guess from startup.
        /// </summary>
        public string BuildUrl(string token, TokenWebSocketServer server)
        {
            var chosenHost = server.BoundPrefixes
                .Select(p => new Uri(p).Host)
                .FirstOrDefault(h => h != "localhost") ?? "localhost";

            var url = $"ws://{chosenHost}:{server.ActualPort}/ws?token={token}";
            _log.Add($"SessionManager: Built URL {url}");
            return url;
        }

        private static string CreateToken()
        {
            // short, URL-safe token
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(6);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static string? GetLocalIPv4()
        {
            // Prefer active Ethernet/WiFi adapters, non-virtual, non-loopback
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                 n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                 n.NetworkInterfaceType != NetworkInterfaceType.Tunnel))
            {
                var ip = ni.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                                      && !IPAddress.IsLoopback(a.Address))?.Address;
                if (ip != null) return ip.ToString();
            }
            // Fallback
            var host = Dns.GetHostAddresses(Dns.GetHostName())
                          .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork
                                            && !IPAddress.IsLoopback(a));
            return host?.ToString();
        }
    }
}
