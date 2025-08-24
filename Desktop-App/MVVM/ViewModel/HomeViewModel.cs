using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MobControlUI.Core;
using MobControlUI.Core.Logging;
using MobControlUI.Core.Net;
using QRCoder;

namespace MobControlUI.MVVM.ViewModel
{
    public class HomeViewModel : ObservableObjects
    {
        private readonly ISessionManager _sessions;
        private readonly TokenWebSocketServer _server;
        private readonly ILogService _log;
        private readonly DispatcherTimer _devicePollTimer;

        private string? _token;
        private string? _sessionUrl;
        private ImageSource? _qrImage;

        private string _webSocketStatus = "Not Running";
        public string WebSocketStatus
        {
            get => _webSocketStatus;
            private set { _webSocketStatus = value; OnPropertyChanged(); }
        }

        private int _connectedDevices;
        public int ConnectedDevices
        {
            get => _connectedDevices;
            private set { _connectedDevices = value; OnPropertyChanged(); }
        }

        public string? SessionUrl
        {
            get => _sessionUrl;
            private set { _sessionUrl = value; OnPropertyChanged(); }
        }

        public ImageSource? QrImage
        {
            get => _qrImage;
            private set { _qrImage = value; OnPropertyChanged(); }
        }

        public ICommand GenerateNewSessionCommand { get; }

        public HomeViewModel(ISessionManager sessions, TokenWebSocketServer server, ILogService log)
        {
            _sessions = sessions;
            _server = server;
            _log = log;

            // Subscribe to dynamic status updates
            _server.OnStatusChanged += (reason, detail) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    WebSocketStatus = MapStatus(reason, detail, _server);
                });
            };

            _log.Add("HomeVM: Starting WebSocket server…");
            var ok = _server.Start();
            WebSocketStatus = MapStatus(
                ok ? TokenWebSocketServer.ServerStatusReason.Listening : _server.StatusReason,
                _server.StatusDetail,
                _server);
            _log.Add(ok ? "HomeVM: WebSocket server started." : "HomeVM: WebSocket server failed to start.");

            // Keep an eye on the number of connected devices
            _devicePollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _devicePollTimer.Tick += (_, __) =>
            {
                try
                {
                    ConnectedDevices = _server.GetDevices().Count();
                }
                catch
                {
                    WebSocketStatus = MapStatus(TokenWebSocketServer.ServerStatusReason.UnknownError, "Server unavailable", _server);
                }
            };
            _devicePollTimer.Start();

            _server.OnFirstClientForToken += Server_OnFirstClientForToken;

            _server.OnDeviceIdentified += (id, token, name) =>
            {
                ConnectedDevices = _server.GetDevices().Count();
                _log.Add($"HomeVM: '{name}' connected (token={token}, id={id})");
            };

            _server.OnLayoutDeclared += (id, title, actions) =>
            {
                var dev = _server.GetDevices().FirstOrDefault(d => d.Id == id);
                var devName = dev?.DeviceName ?? "(device)";
                _log.Add($"HomeVM: Layout '{title}' from '{devName}' with [{string.Join(", ", actions)}]");
            };

            GenerateNewSessionCommand = new RelayCommand(_ => GenerateNew());
            GenerateNew();
        }

        private void Server_OnFirstClientForToken(string token)
        {
            // Rotate only when the currently shown token is consumed
            if (!string.Equals(token, _token, StringComparison.Ordinal)) return;

            _log.Add($"HomeVM: First client connected for token {token} → rotating token");
            // UI update must run on the UI thread
            Application.Current?.Dispatcher.Invoke(GenerateNew);
        }

        private void GenerateNew()
        {
            if (_token != null)
            {
                _log.Add($"HomeVM: Closing previous token={_token}");
                _sessions.CloseSession(_token);
            }

            _token = _sessions.CreateSession();
            SessionUrl = _sessions.BuildUrl(_token, _server);
            _log.Add($"HomeVM: New session → {SessionUrl}");

            QrImage = MakeQr(SessionUrl!);
            _log.Add("HomeVM: QR generated");
        }


        private static ImageSource MakeQr(string text)
        {
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
            var png = new PngByteQRCode(data);
            var bytes = png.GetGraphic(20);
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private static string MapStatus(TokenWebSocketServer.ServerStatusReason reason, string? detail, TokenWebSocketServer server)
        {
            return reason switch
            {
                TokenWebSocketServer.ServerStatusReason.Listening => "Running",
                TokenWebSocketServer.ServerStatusReason.Starting => "Starting",
                TokenWebSocketServer.ServerStatusReason.PortConflict => "Port Error",
                TokenWebSocketServer.ServerStatusReason.AclDenied => "Permission Error",
                TokenWebSocketServer.ServerStatusReason.NoEligibleAdapters => "Adapter Error",
                TokenWebSocketServer.ServerStatusReason.NetworkUnavailable => "Network Error",
                TokenWebSocketServer.ServerStatusReason.UnknownError => "Error",
                TokenWebSocketServer.ServerStatusReason.Stopped => "Stopped",
                _ => "Unknown"
            };
        }
    }
}