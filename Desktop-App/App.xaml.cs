using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MobControlUI.Core.Logging;
using MobControlUI.Core.Mapping;
using MobControlUI.Core.Net;
using MobControlUI.Core.Players;
using MobControlUI.Core.Storage;
using MobControlUI.Core.Sync;
using MobControlUI.Core.Input;
using MobControlUI.MVVM.ViewModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace MobControlUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IHost? _host;

        // Expose DI container to the rest of the app (e.g., MainWindow)
        public IServiceProvider Services => _host!.Services;

        public App()
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    // Ensure mappings dir exists before services that rely on it
                    AppPaths.EnsureCreated();

                    // Services
                    services.AddSingleton<ILogService, LogService>();
                    services.AddSingleton<ISessionManager, SessionManager>();
                    services.AddSingleton<TokenWebSocketServer>();
                    services.AddSingleton<IInputMappingStore, InputMappingStore>();
                    services.AddSingleton<IMappingCatalog, MappingCatalog>();
                    services.AddSingleton<ILayoutMappingRegistry, LayoutMappingRegistry>();
                    services.AddSingleton<MobControlUI.Core.UI.IMessageService, MobControlUI.Core.UI.MessageBoxService>();
                    services.AddSingleton<IPlayerIdAllocator, PlayerIdAllocator>();
                    services.AddSingleton<IActiveMappingService, ActiveMappingService>();
                    services.AddSingleton<InputEventRouter>();

                    // ViewModels
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<HomeViewModel>();
                    services.AddSingleton<ControllerViewModel>();
                    services.AddSingleton<CreateMappingViewModel>();
                    services.AddSingleton<ViewMappingsViewModel>();
                    services.AddSingleton<LogsViewModel>();

                    // Editor (one instance per edit session)
                    services.AddTransient<UpdateMappingViewModel>();

                    // Windows
                    services.AddSingleton<MainWindow>();

                    // --- RTDB folder sync (REST) ---
                    services.AddSingleton(new RtdbSyncOptions
                    {
                        BaseUrl = "https://fyp-mob-controller-default-rtdb.asia-southeast1.firebasedatabase.app",
                        AuthToken = null,          // set an ID token if your rules require auth
                        UseLastWriteWins = true
                        // MirrorDeletes defaults to true in your sync code
                    });

                    // Typed HttpClient for the sync service WITH timeout
                    services.AddHttpClient<IFirebaseRtdbFolderSync, FirebaseRtdbFolderSync>(c =>
                    {
                        c.Timeout = TimeSpan.FromSeconds(5);
                    });
                })
                .Build();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            await _host!.StartAsync();

            // Start WS Server
            var ws = _host.Services.GetRequiredService<TokenWebSocketServer>();
            ws.Start();

            // ensure router is created so it subscribes to WS events
            _ = _host.Services.GetRequiredService<InputEventRouter>();

#if DEBUG
            try
            {
                // (A) Seed defaults into the mappings folder used by the catalog
                var mappingsRoot = AppPaths.MappingsDir;      // your APPDATA path
                Directory.CreateDirectory(mappingsRoot);

                // locate your repo defaults or bin\.\Storage\Defaults
                string? defaultsRoot = null;
                var candidate1 = Path.Combine(AppContext.BaseDirectory, "Storage", "Defaults");
                if (Directory.Exists(candidate1) &&
                    Directory.EnumerateFiles(candidate1, "*.json").Any())
                {
                    defaultsRoot = candidate1;
                }
                else
                {
                    var dir = new DirectoryInfo(AppContext.BaseDirectory);
                    for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                    {
                        var candidate = Path.Combine(dir.FullName, "Storage", "Defaults");
                        if (Directory.Exists(candidate) &&
                            Directory.EnumerateFiles(candidate, "*.json").Any())
                        {
                            defaultsRoot = candidate;
                            break;
                        }
                    }
                }

                if (defaultsRoot != null)
                {
                    foreach (var src in Directory.EnumerateFiles(defaultsRoot, "*.json"))
                    {
                        var dst = Path.Combine(mappingsRoot, Path.GetFileName(src));
                        File.Copy(src, dst, overwrite: true);
                    }
                }

                // (B) quick debug check
                var files = Directory.EnumerateFiles(AppPaths.MappingsDir, "*.json").ToArray();
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Mappings in {AppPaths.MappingsDir}: {files.Length}");
                foreach (var f in files) System.Diagnostics.Debug.WriteLine("  - " + f);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] Seeding error: " + ex);
            }
#endif

            // Ensure local base folder exists
            AppPaths.EnsureCreated();

            // --- Startup sync: configure pairs, then RTDB → local ---
            try
            {
                var opt = _host.Services.GetRequiredService<RtdbSyncOptions>();
                opt.Pairs.Clear();

                // (1) Existing mappings pair
                opt.Pairs.Add(new RtdbFolderPair(AppPaths.MappingsDir, "mappings"));

                // (2) NEW: associations pair — only this file from %AppData%\MobControlUI
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); // Roaming
                var assocDir = Path.Combine(appData, "MobControlUI");
                Directory.CreateDirectory(assocDir); // defensive
                opt.Pairs.Add(new RtdbFolderPair(assocDir, "associations")
                {
                    IncludeFiles = new List<string> { "LayoutMappingAssociations.json" } // keep exact name
                });

                var sync = _host.Services.GetRequiredService<IFirebaseRtdbFolderSync>();
                await sync.DownloadAllAsync();
            }
            catch (Exception ex)
            {
                _host.Services.GetRequiredService<ILogService>()
                    .Add($"Sync (startup) failed: {ex.Message}", "Error");
            }

            var window = _host.Services.GetRequiredService<MainWindow>();
            window.DataContext = _host.Services.GetRequiredService<MainViewModel>();
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                var server = _host!.Services.GetService<TokenWebSocketServer>();
                server?.Stop();
            }
            catch { /* ignore */ }

            _host!.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _host!.Dispose();
            base.OnExit(e);
        }
    }
}
