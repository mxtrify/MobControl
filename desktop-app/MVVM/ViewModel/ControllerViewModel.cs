using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MobControlUI.Core;
using MobControlUI.Core.Mapping;
using MobControlUI.Core.Net;
using MobControlUI.Core.Storage;
using MobControlUI.Core.UI;

namespace MobControlUI.MVVM.ViewModel
{
    public sealed class ControllerViewModel : ObservableObjects
    {
        private readonly TokenWebSocketServer _server;
        private readonly IMappingCatalog _mappings;
        private readonly ILayoutMappingRegistry _assoc;
        private readonly IMessageService _msg;
        private readonly IActiveMappingService _activeMappings;

        public ObservableCollection<DeviceRowViewModel> Devices { get; } = new();
        public ReadOnlyObservableCollection<string> MappingNames => _mappings.Names;

        // Track taken Player IDs
        private readonly HashSet<int> _taken = new();

        public ControllerViewModel(TokenWebSocketServer server,
                                   IMappingCatalog mappings,
                                   ILayoutMappingRegistry assoc,
                                   IMessageService msg,
                                   IActiveMappingService activeMappings)
        {
            _server = server;
            _mappings = mappings;
            _assoc = assoc;
            _msg = msg;
            _activeMappings = activeMappings;

            // Start catalog before rows bind to Names
            _mappings.Start();

            // Seed any devices already connected before this VM was created
            foreach (var d in _server.GetDevices())
                AddDevice(d.Id, d.DeviceName ?? $"Device {Devices.Count + 1}", d.LayoutTitle);

            // Live: device identified → add row (guard against duplicates)
            _server.OnDeviceIdentified += (id, token, name) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (Devices.Any(x => x.Id == id)) return; // already present
                    AddDevice(id, name);
                });
            };

            // Live: device declares/changes layout → update row (DO NOT auto-select mapping)
            _server.OnLayoutDeclared += (id, title, actions) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var row = Devices.FirstOrDefault(x => x.Id == id);
                    if (row != null)
                    {
                        row.LayoutTitle = title;
                    }
                });
            };

            // Live: disconnect → remove row & free player id + active mapping
            _server.OnDeviceDisconnected += (id, name) =>
            {
                Application.Current?.Dispatcher.Invoke(() => RemoveDevice(id));
            };
        }

        private void AddDevice(Guid id, string deviceName, string? layoutTitle = null)
        {
            // extra guard, in case this is called directly
            if (Devices.Any(x => x.Id == id)) return;

            var row = new DeviceRowViewModel(id, deviceName, MappingNames, _assoc)
            {
                LayoutTitle = layoutTitle,
                // Ensure mapping shows as blank on first appearance
                SelectedMapping = null
            };

            row.MappingChanged += OnMappingChangedAsync;
            row.PlayerIdChanged += OnPlayerIdChanged;
            row.DisconnectRequested += OnDisconnect;

            Devices.Add(row);
            RecomputePlayerOptions();
        }

        private void RemoveDevice(Guid id)
        {
            var row = Devices.FirstOrDefault(d => d.Id == id);
            if (row == null) return;

            // Unsubscribe to avoid leaks
            row.MappingChanged -= OnMappingChangedAsync;
            row.PlayerIdChanged -= OnPlayerIdChanged;
            row.DisconnectRequested -= OnDisconnect;
            row.Detach(); // unsubscribe from association change events inside the row

            if (row.PlayerId.HasValue) _taken.Remove(row.PlayerId.Value);
            _activeMappings.Clear(row.Id);

            Devices.Remove(row);
            RecomputePlayerOptions();
        }

        // ---- Mapping selection changed ---------------------------------------------------------

        private async void OnMappingChangedAsync(DeviceRowViewModel row, string? mapping)
        {
            // If user picked the placeholder/null, clear active mapping and return
            if (string.IsNullOrWhiteSpace(mapping))
            {
                _activeMappings.Clear(row.Id);
                return;
            }

            var layout = row.LayoutTitle ?? string.Empty;
            if (string.IsNullOrWhiteSpace(layout))
            {
                _msg.Warn("This device hasn't reported a layout yet.\nYou can select a mapping after a layout is chosen.");
                await ResetMappingToDefaultAsync(row);
                return;
            }

            // Validate association: layout ↔ mapping must exist
            var ok = await _assoc.IsAssociatedAsync(layout, mapping);
            if (!ok)
            {
                _msg.Warn($"Mapping '{mapping}' is not associated with layout '{layout}'.");
                await ResetMappingToDefaultAsync(row);
                return;
            }

            // Apply mapping (remember as active for this device)
            _activeMappings.Set(row.Id, mapping);
        }

        private static async Task ResetMappingToDefaultAsync(DeviceRowViewModel row)
        {
            var app = Application.Current;
            if (app?.Dispatcher != null)
            {
                await app.Dispatcher.InvokeAsync(
                    () => row.SelectedMapping = null,
                    DispatcherPriority.Background);
            }
            else
            {
                row.SelectedMapping = null;
            }
        }

        // ---- Player ID changed -----------------------------------------------------------------

        private void OnPlayerIdChanged(DeviceRowViewModel row, int? oldId, int? newId)
        {
            // release previous
            if (oldId.HasValue) _taken.Remove(oldId.Value);

            // reserve new (if any)
            if (newId.HasValue)
            {
                if (_taken.Contains(newId.Value))
                {
                    // Should not happen due to filtered options, but guard anyway
                    _msg.Warn($"Player ID {newId.Value} is already taken.");
                    row.PlayerId = null;
                }
                else
                {
                    _taken.Add(newId.Value);
                }
            }

            RecomputePlayerOptions();
        }

        private void RecomputePlayerOptions()
        {
            var max = Math.Max(Devices.Count, 1);
            var takenSnapshot = _taken.ToArray();

            foreach (var d in Devices)
                d.UpdatePlayerIdSet(max, takenSnapshot);
        }

        private void OnDisconnect(DeviceRowViewModel row)
        {
            _activeMappings.Clear(row.Id);
            _server.Disconnect(row.Id);
        }

        /* ---- Auto-select preferred mapping (kept but unused) -----------------------------------

        private async Task TryAutoSelectPreferredAsync(DeviceRowViewModel row)
        {
            if (string.IsNullOrWhiteSpace(row.LayoutTitle) || !string.IsNullOrWhiteSpace(row.SelectedMapping))
                return;

            var layout = row.LayoutTitle!;
            var preferred = await _prefs.GetPreferredMappingAsync(row.DeviceName, layout);
            if (string.IsNullOrWhiteSpace(preferred)) return;

            var existsLocally = _mappings.Names.Contains(preferred);
            var associated = await _assoc.IsAssociatedAsync(layout, preferred);
            if (existsLocally && associated)
            {
                row.SelectedMapping = preferred;
            }
        }*/
    }
}