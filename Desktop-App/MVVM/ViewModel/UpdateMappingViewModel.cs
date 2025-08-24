using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows; // NEW: for Application.Current.Dispatcher
using MobControlUI.Core;
using MobControlUI.Core.Mapping;
using MobControlUI.Core.Storage;
using MobControlUI.Core.UI;
using MobControlUI.Core.Net; // NEW: for TokenWebSocketServer

namespace MobControlUI.MVVM.ViewModel
{
    public sealed class UpdateMappingViewModel : ObservableObjects
    {
        private readonly MainViewModel _main;
        private readonly IInputMappingStore _store;
        private readonly IMessageService _msg;
        private readonly ILayoutMappingRegistry _assoc;
        private readonly TokenWebSocketServer _server; // NEW

        public UpdateMappingViewModel(MainViewModel main,
                                      IInputMappingStore store,
                                      IMessageService msg,
                                      ILayoutMappingRegistry assoc,
                                      TokenWebSocketServer server) // NEW
        {
            _main = main;
            _store = store;
            _msg = msg;
            _assoc = assoc;
            _server = server; // NEW

            CancelCommand = new RelayCommand(_ => _main.NavigateToViewMappings());
            DeleteCommand = new RelayCommand(async _ => await DeleteAsync());
            SaveCommand = new RelayCommand(async _ => await SaveAsync());

            ActionRows.CollectionChanged += (_, __) => OnPropertyChanged(nameof(HasActions));

            // 🔊 Listen for live layout updates; keep the editor in sync
            _server.OnLayoutDeclared += (id, title, actions) =>
            {
                if (!string.Equals(LayoutTitle, title, StringComparison.OrdinalIgnoreCase))
                    return; // not the layout we're editing

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    ReconcileActionsWithLayout(actions ?? Array.Empty<string>());
                });
            };
        }

        // ---------- Header fields ----------
        private string _layoutTitle = "";
        public string LayoutTitle
        {
            get => _layoutTitle;
            private set { if (_layoutTitle == value) return; _layoutTitle = value; OnPropertyChanged(); }
        }

        private string _fileName = "";
        /// <summary>Name shown in the textbox (without .json preferred).</summary>
        public string FileName
        {
            get => _fileName;
            set { if (_fileName == value) return; _fileName = value; OnPropertyChanged(); }
        }

        private string _originalNameNoExt = ""; // normalized original name (no .json)

        // ---------- Action rows ----------
        public ObservableCollection<ActionRowVM> ActionRows { get; } = new();
        public bool HasActions => ActionRows.Count > 0;

        // ---------- Commands / Navigation ----------
        public RelayCommand SaveCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public event Action? RequestClose;

        // ---------- Public API ----------
        /// <summary>Load an existing mapping by file name (with or without .json).</summary>
        public async Task LoadAsync(string mappingFileName)
        {
            _originalNameNoExt = NormalizeName(mappingFileName);
            FileName = _originalNameNoExt; // show without .json

            // Uses your flexible loader that can read the unified schema or legacy
            var file = await _store.LoadFlexibleAsync(_originalNameNoExt);
            if (file == null)
            {
                _msg.Warn($"Mapping '{mappingFileName}' could not be loaded.");
                return;
            }

            LayoutTitle = file.Layout ?? "";

            // FIFO: keep whatever order the JSON had; do NOT sort
            ActionRows.Clear();
            foreach (var kv in file.Bindings)
                ActionRows.Add(new ActionRowVM(kv.Key, kv.Value));

            // 🔄 Initial reconcile against latest known layout actions (if any devices reported it)
            var latest = GetLatestLayoutActions();
            if (latest.Length > 0)
                ReconcileActionsWithLayout(latest);

            OnPropertyChanged(nameof(HasActions));
        }

        // ---------- Save ----------
        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(FileName))
            {
                _msg.Warn("Please enter a mapping file name.");
                return;
            }

            // 🔒 Before saving, reconcile with the latest layout to ensure "proper update"
            var latest = GetLatestLayoutActions();
            if (latest.Length > 0)
                ReconcileActionsWithLayout(latest);

            var newNameNoExt = NormalizeName(FileName);
            var oldNameNoExt = _originalNameNoExt;
            var isRename = !newNameNoExt.Equals(oldNameNoExt, StringComparison.OrdinalIgnoreCase);

            // If rename, ensure destination doesn’t already exist
            if (isRename)
            {
                var existing = await _store.ListAsync(); // names without .json
                if (existing.Any(n => n.Equals(newNameNoExt, StringComparison.OrdinalIgnoreCase)))
                {
                    _msg.Warn($"A mapping named '{FileName}' already exists. Please choose a different name.");
                    return;
                }
            }

            // Build DTO from the editor (unified on-disk schema)
            var dto = new InputMappingFile
            {
                Version = 1,
                Layout = LayoutTitle ?? "",
                // preserve the (current) order shown to the user
                Bindings = ActionRows.ToDictionary(a => a.ActionName, a => a.KeyBinding ?? "")
            };

            // Save content
            await _store.SaveAsync(newNameNoExt, dto);

            // If rename: delete old file and update associations for this layout
            if (isRename)
            {
                await _store.DeleteAsync(oldNameNoExt);

                await _assoc.RemoveAsync(LayoutTitle!, oldNameNoExt);
                await _assoc.AssociateAsync(LayoutTitle!, newNameNoExt);

                _originalNameNoExt = newNameNoExt;
            }

            _msg.Info("Mapping updated.");
            _main.NavigateToViewMappings();
            RequestClose?.Invoke(); // signal close if the host window/dialog uses it
        }

        // ---------- Delete ----------
        private async Task DeleteAsync()
        {
            if (!_msg.Confirm($"Delete mapping '{_originalNameNoExt}.json'?\nThis cannot be undone."))
                return;

            await _store.DeleteAsync(_originalNameNoExt);
            await _assoc.RemoveMappingEverywhereAsync(_originalNameNoExt);

            _msg.Info("Mapping deleted.");
            _main.NavigateToViewMappings();
            RequestClose?.Invoke();
        }

        // ---------- Helpers ----------
        private static string NormalizeName(string name)
            => Path.GetFileNameWithoutExtension(name ?? string.Empty).Trim();

        /// <summary>
        /// Get the latest distinct actions for the currently edited layout,
        /// based on all connected devices that reported this layout.
        /// </summary>
        private string[] GetLatestLayoutActions()
        {
            var title = LayoutTitle;
            if (string.IsNullOrWhiteSpace(title)) return Array.Empty<string>();

            return _server.GetDevices()
                          .Where(d => !string.IsNullOrWhiteSpace(d.LayoutTitle) &&
                                      string.Equals(d.LayoutTitle, title, StringComparison.OrdinalIgnoreCase) &&
                                      d.Actions != null && d.Actions.Length > 0)
                          .SelectMany(d => d.Actions)
                          .Where(a => !string.IsNullOrWhiteSpace(a))
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .ToArray();
        }

        /// <summary>
        /// Properly updates the editor rows to match the provided layout actions:
        /// - Adds any new actions (as blank bindings).
        /// - Removes any rows for actions that no longer exist.
        /// - Keeps bindings for actions that remain.
        /// </summary>
        private void ReconcileActionsWithLayout(IEnumerable<string> latestActions)
        {
            var latest = new List<string>(latestActions ?? Array.Empty<string>());
            var latestSet = new HashSet<string>(latest, StringComparer.OrdinalIgnoreCase);

            // Remove stale rows (iterate backwards)
            for (int i = ActionRows.Count - 1; i >= 0; i--)
            {
                if (!latestSet.Contains(ActionRows[i].ActionName))
                    ActionRows.RemoveAt(i);
            }

            // Add any missing actions (as blank)
            var existing = new HashSet<string>(ActionRows.Select(r => r.ActionName), StringComparer.OrdinalIgnoreCase);
            foreach (var a in latest)
            {
                if (!existing.Contains(a))
                    ActionRows.Add(new ActionRowVM(a, null));
            }

            OnPropertyChanged(nameof(HasActions));
        }

        // ---------- Row VM ----------
        public sealed class ActionRowVM : ObservableObjects
        {
            public string ActionName { get; }
            private string? _keyBinding;
            public string? KeyBinding
            {
                get => _keyBinding;
                set { if (_keyBinding == value) return; _keyBinding = value; OnPropertyChanged(); }
            }

            public ActionRowVM(string action, string? binding)
            {
                ActionName = action;
                _keyBinding = binding;
            }
        }

        // Call this to sync the editor rows with the latest layout reported by devices.
        public void RefreshAgainstLiveLayout()
        {
            var latest = GetLatestLayoutActions();
            if (latest.Length > 0)
                ReconcileActionsWithLayout(latest);
        }

    }
}