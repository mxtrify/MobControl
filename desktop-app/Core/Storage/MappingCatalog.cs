using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace MobControlUI.Core.Storage
{
    public interface IMappingCatalog
    {
        ReadOnlyObservableCollection<string> Names { get; }
        void Start();
        void Refresh();  // ← allow manual refresh (e.g., after a save if no watcher yet)
    }

    public sealed class MappingCatalog : IMappingCatalog, IDisposable
    {
        private readonly ObservableCollection<string> _names = new();
        private readonly ReadOnlyObservableCollection<string> _readOnly;
        private FileSystemWatcher? _watcher;

        // Debounce FS events (Created/Changed/Renamed often fire in bursts)
        private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };

        public MappingCatalog()
        {
            _readOnly = new(_names);
            _debounce.Tick += (_, __) =>
            {
                _debounce.Stop();
                RefreshOnUi();
            };
        }

        public ReadOnlyObservableCollection<string> Names => _readOnly;

        public void Start()
        {
            if (_watcher != null) return;

            AppPaths.EnsureCreated();
            ScanNow();

            _watcher = new FileSystemWatcher(AppPaths.MappingsDir, "*.json")
            {
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };

            _watcher.Created += OnFsChange;
            _watcher.Changed += OnFsChange;
            _watcher.Renamed += OnFsChange;
            _watcher.Deleted += OnFsChange;
        }

        public void Refresh() => RefreshOnUi();

        private void OnFsChange(object? sender, FileSystemEventArgs e)
        {
            // Coalesce multiple rapid events
            _debounce.Stop();
            _debounce.Start();
        }

        private void RefreshOnUi()
        {
            var disp = Application.Current?.Dispatcher;
            if (disp is null || disp.CheckAccess())
            {
                ScanNow();
            }
            else
            {
                // Use InvokeAsync to avoid blocking UI (ok if you prefer Invoke)
                disp.InvokeAsync(ScanNow, DispatcherPriority.Background);
            }
        }

        private void ScanNow()
        {
            AppPaths.EnsureCreated();

            var found = Directory.EnumerateFiles(AppPaths.MappingsDir, "*.json")
                                 .Select(Path.GetFileNameWithoutExtension)
                                 .Where(n => !string.IsNullOrWhiteSpace(n))
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                 .ToArray();

            // Minimal churn: if unchanged, do nothing
            if (_names.Count == found.Length &&
                _names.SequenceEqual(found, StringComparer.OrdinalIgnoreCase))
                return;

            _names.Clear();
            foreach (var n in found) _names.Add(n!);
        }

        public void Dispose()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFsChange;
                _watcher.Changed -= OnFsChange;
                _watcher.Renamed -= OnFsChange;
                _watcher.Deleted -= OnFsChange;
                _watcher.Dispose();
                _watcher = null;
            }
            _debounce.Stop();
            _debounce.Tick -= (_, __) => { }; // no-op; just being explicit
        }
    }
}