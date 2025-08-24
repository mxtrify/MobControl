using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using MobControlUI.Core;

// Use the same interface as ControllerViewModel
using ILayoutMappingRegistry = MobControlUI.Core.Storage.ILayoutMappingRegistry;

namespace MobControlUI.MVVM.ViewModel
{
    public sealed class DeviceRowViewModel : ObservableObjects
    {
        // Small value/display pair so ComboBox can bind by value (SelectedValue)
        public sealed record Choice<T>(T Value, string Display);

        public Guid Id { get; }
        public string DeviceName { get; }

        private readonly ReadOnlyObservableCollection<string> _catalogNames;
        private readonly ILayoutMappingRegistry _assoc;

        public DeviceRowViewModel(Guid id,
                                  string deviceName,
                                  ReadOnlyObservableCollection<string> mappingCatalogNames,
                                  ILayoutMappingRegistry assoc)
        {
            Id = id;
            DeviceName = deviceName;
            _catalogNames = mappingCatalogNames;
            _assoc = assoc;

            // Build initial mapping choices (async)
            _ = RebuildMappingOptionsAsync();

            // React to catalog changes (any thread) → rebuild
            if (_catalogNames is INotifyCollectionChanged ncc)
                ncc.CollectionChanged += async (_, __) => await RebuildMappingOptionsAsync();

            // React to association file changes → only rebuild if it affects our current layout
            _assoc.AssociationsChanged += OnAssociationsChanged;

            // Player IDs: create stable list once, then FILTER instead of rebuild
            _playerIdChoices.Add(new Choice<int?>(null, " ")); // blank first option; overlay shows prompt
            _playerIdView = CollectionViewSource.GetDefaultView(_playerIdChoices);
            _playerIdView.Filter = PlayerIdFilter;

            DisconnectCommand = new RelayCommand(_ => DisconnectRequested?.Invoke(this));
        }

        // If you remove a row and want to unsubscribe explicitly, call this.
        public void Detach()
        {
            _assoc.AssociationsChanged -= OnAssociationsChanged;
        }

        private async void OnAssociationsChanged(string layoutTitle)
        {
            var current = _layoutTitle;
            if (string.IsNullOrWhiteSpace(current)) return;
            if (!string.Equals(current, layoutTitle, StringComparison.OrdinalIgnoreCase)) return;

            await RebuildMappingOptionsAsync();
        }

        // ───────────────────────── Layout title: re-filter mapping list ─────────────────────────

        private string? _layoutTitle;
        public string? LayoutTitle
        {
            get => _layoutTitle;
            set
            {
                if (_layoutTitle == value) return;
                _layoutTitle = value;
                OnPropertyChanged();
                _ = RebuildMappingOptionsAsync(); // re-filter for this layout
            }
        }

        // ───────────────────────────────────── Mapping (bind-by-value) ─────────────────────────────────────

        private readonly ObservableCollection<Choice<string?>> _mappingChoices = new();
        public ObservableCollection<Choice<string?>> MappingChoices => _mappingChoices;

        private bool _isUpdatingMappingOptions;

        private string? _selectedMapping;
        public string? SelectedMapping
        {
            get => _selectedMapping;
            set
            {
                if (_selectedMapping == value) return;
                _selectedMapping = value;
                OnPropertyChanged();
                if (!_isUpdatingMappingOptions)
                    MappingChanged?.Invoke(this, value);
            }
        }

        /// <summary>
        /// Rebuild mapping list so it only contains mappings allowed for the current layout.
        /// If a layout exists and no valid selection is present, auto-select the first allowed mapping.
        /// If no layout yet or no allowed mappings, show only the blank placeholder.
        /// </summary>
        private async Task RebuildMappingOptionsAsync()
        {
            var layout = string.IsNullOrWhiteSpace(_layoutTitle) ? null : _layoutTitle!.Trim();
            var currentSelection = _selectedMapping;

            List<string> allowed = new();

            // If we have a layout, keep only catalog names associated with it.
            var candidates = _catalogNames.ToArray();
            if (layout is not null)
            {
                var checks = candidates.Select(async name =>
                    (name, ok: await _assoc.IsAssociatedAsync(layout, name))).ToArray();

                var results = await Task.WhenAll(checks);
                allowed = results.Where(r => r.ok).Select(r => r.name).ToList();
            }
            // else: no layout → show only placeholder

            // Build the desired display list
            var desired = new List<Choice<string?>>(1 + allowed.Count)
    {
        new Choice<string?>(null, " ") // placeholder/reset (blank line; hint overlay in XAML)
    };
            foreach (var name in allowed)
                desired.Add(new Choice<string?>(name, name));

            // --- Selection rule ---
            // 1) If current selection is still valid, keep it.
            // 2) Else, if we have a layout and at least one allowed mapping, auto-select the first.
            // 3) Else, fall back to placeholder (null).
            string? newSelected =
                (currentSelection is not null && allowed.Contains(currentSelection))
                    ? currentSelection
                    : (layout is not null && allowed.Count > 0 ? allowed[0] : null);

            await ApplyMappingChoicesAsync(desired, newSelected);
        }


        // Expose a refresh hook (e.g., ControllerViewModel can call it if needed)
        public Task RefreshMappingChoicesAsync() => RebuildMappingOptionsAsync();

        private async Task ApplyMappingChoicesAsync(
            List<Choice<string?>> desired, string? newSelected)
        {
            bool equal =
                desired.Count == _mappingChoices.Count &&
                desired.Select(x => x.Value).SequenceEqual(_mappingChoices.Select(x => x.Value));

            if (equal)
            {
                OnPropertyChanged(nameof(SelectedMapping));
                if (Application.Current?.Dispatcher != null)
                    await Application.Current.Dispatcher.InvokeAsync(
                        () => OnPropertyChanged(nameof(SelectedMapping)),
                        DispatcherPriority.ContextIdle);
                return;
            }

            _isUpdatingMappingOptions = true;
            try
            {
                // Update the list on the UI thread
                if (Application.Current?.Dispatcher?.CheckAccess() == true)
                {
                    _mappingChoices.Clear();
                    foreach (var c in desired) _mappingChoices.Add(c);
                }
                else if (Application.Current?.Dispatcher != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        _mappingChoices.Clear();
                        foreach (var c in desired) _mappingChoices.Add(c);
                    });
                }
                else
                {
                    _mappingChoices.Clear();
                    foreach (var c in desired) _mappingChoices.Add(c);
                }

                // assign new selection
                var old = _selectedMapping;
                _selectedMapping = newSelected;
                OnPropertyChanged(nameof(SelectedMapping));

                if (Application.Current?.Dispatcher != null)
                    await Application.Current.Dispatcher.InvokeAsync(
                        () => OnPropertyChanged(nameof(SelectedMapping)),
                        DispatcherPriority.ContextIdle);

                // 🚀 if selection actually changed, schedule MappingChanged manually
                if (old != newSelected && newSelected != null)
                {
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        MappingChanged?.Invoke(this, newSelected);
                    }), DispatcherPriority.Background);
                }
            }
            finally
            {
                _isUpdatingMappingOptions = false;
            }
        }


        public event Action<DeviceRowViewModel, string?>? MappingChanged;

        // ─────────────────────────────── Player ID (same working pattern) ───────────────────────────────

        private readonly ObservableCollection<Choice<int?>> _playerIdChoices = new();
        public ObservableCollection<Choice<int?>> PlayerIdChoices => _playerIdChoices;

        private readonly ICollectionView _playerIdView;
        private readonly HashSet<int> _taken = new();
        private int _maxIds = 1;
        private bool _isUpdatingPlayerOptions;
        private int? _playerId;

        public int? PlayerId
        {
            get => _playerId;
            set
            {
                if (_playerId == value) return;
                var old = _playerId;
                _playerId = value;
                OnPropertyChanged();

                if (!_isUpdatingPlayerOptions)
                    PlayerIdChanged?.Invoke(this, old, value);

                _playerIdView.Refresh(); // keep our own selection visible
            }
        }

        public event Action<DeviceRowViewModel, int?, int?>? PlayerIdChanged;
        public event Action<DeviceRowViewModel>? DisconnectRequested;

        public RelayCommand DisconnectCommand { get; }

        public void UpdatePlayerIdSet(int max, IEnumerable<int> taken)
        {
            _isUpdatingPlayerOptions = true;
            try
            {
                _maxIds = Math.Max(1, max);

                _taken.Clear();
                foreach (var t in taken) _taken.Add(t);

                // ensure entries 1.._maxIds exist
                for (int i = 1; i <= _maxIds; i++)
                {
                    if (!_playerIdChoices.Any(c => c.Value == i))
                        _playerIdChoices.Add(new Choice<int?>(i, i.ToString()));
                }

                // remove entries > max (but preserve our own selection)
                for (int i = _playerIdChoices.Count - 1; i >= 0; i--)
                {
                    var v = _playerIdChoices[i].Value;
                    if (v is int iv && iv > _maxIds && PlayerId != iv)
                        _playerIdChoices.RemoveAt(i);
                }

                // keep null at index 0
                var idxNull = -1;
                for (int i = 0; i < _playerIdChoices.Count; i++)
                    if (_playerIdChoices[i].Value is null) { idxNull = i; break; }
                if (idxNull < 0) _playerIdChoices.Insert(0, new Choice<int?>(null, " "));
                else if (idxNull != 0) _playerIdChoices.Move(idxNull, 0);

                _playerIdView.Refresh();

                OnPropertyChanged(nameof(PlayerId));
                if (Application.Current?.Dispatcher != null)
                    _ = Application.Current.Dispatcher.InvokeAsync(
                        () => OnPropertyChanged(nameof(PlayerId)),
                        DispatcherPriority.ContextIdle);
            }
            finally
            {
                _isUpdatingPlayerOptions = false;
            }
        }

        private bool PlayerIdFilter(object o)
        {
            if (o is not Choice<int?> c) return false;
            if (c.Value is null) return true;
            int v = c.Value.Value;
            return !_taken.Contains(v) || PlayerId == v;
        }
    }
}
