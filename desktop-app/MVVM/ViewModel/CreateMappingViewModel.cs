using MobControlUI.Core;
using MobControlUI.Core.Mapping;
using MobControlUI.Core.Net;
using MobControlUI.Core.Storage;
using MobControlUI.Core.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ILayoutMappingRegistry = MobControlUI.Core.Storage.ILayoutMappingRegistry;

namespace MobControlUI.MVVM.ViewModel
{
    public sealed class CreateMappingViewModel : ObservableObjects
    {
        // Same value/display pair we used elsewhere so ComboBox can bind by value.
        public sealed record Choice<T>(T Value, string Display);

        private readonly TokenWebSocketServer _server;
        private readonly IInputMappingStore _store;
        private readonly ILayoutMappingRegistry _assoc;
        private readonly IMessageService _msg;
        private readonly IMappingCatalog _mappings;

        // Per device: latest (layout, actions) we know about.
        private readonly Dictionary<Guid, (string Layout, string[] Actions)> _byDevice = new();

        // Source list of distinct layout titles (internal).
        public ObservableCollection<string> LayoutOptions { get; } = new();

        // What the ComboBox binds to (first item = null/blank).
        public ObservableCollection<Choice<string?>> LayoutChoices { get; } = new();

        // Rows shown in the editor box.
        public ObservableCollection<ActionRow> ActionRows { get; } = new();
        public bool HasActions => ActionRows.Count > 0;

        private string? _selectedLayout;
        public string? SelectedLayout
        {
            get => _selectedLayout;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
                if (_selectedLayout == normalized) return;
                _selectedLayout = normalized;
                OnPropertyChanged();
                _ = PopulateRowsForSelectedLayoutAsync();   // fire & forget
                RaiseCanSave();
            }
        }

        private string? _fileName;
        public string? FileName
        {
            get => _fileName;
            set { if (_fileName == value) return; _fileName = value; OnPropertyChanged(); RaiseCanSave(); }
        }

        public RelayCommand SaveCommand { get; }
        public RelayCommand ClearCommand { get; }

        public CreateMappingViewModel(TokenWebSocketServer server,
                                      IInputMappingStore store,
                                      ILayoutMappingRegistry assoc,
                                      IMessageService msg,
                                      IMappingCatalog mappings)
        {
            _server = server;
            _store = store;
            _assoc = assoc;
            _msg = msg;
            _mappings = mappings;

            // Seed with any devices that already reported a layout.
            foreach (var d in _server.GetDevices())
                if (!string.IsNullOrWhiteSpace(d.LayoutTitle) && d.Actions is { Length: > 0 })
                    _byDevice[d.Id] = (d.LayoutTitle!, d.Actions);

            RebuildLayoutOptions(); // also builds LayoutChoices

            // Device declares/changes layout → update list; refresh rows if the same layout is selected.
            _server.OnLayoutDeclared += (id, title, actions) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _byDevice[id] = (title, actions);
                    RebuildLayoutOptions();
                    if (!string.IsNullOrWhiteSpace(SelectedLayout) &&
                        string.Equals(SelectedLayout, title, StringComparison.OrdinalIgnoreCase))
                    {
                        _ = PopulateRowsForSelectedLayoutAsync();
                    }
                });
            };

            // Device disconnects → remove its contribution; clear rows if our selected layout disappears.
            _server.OnDeviceDisconnected += (id, _) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (_byDevice.Remove(id))
                    {
                        RebuildLayoutOptions();
                        if (!LayoutOptions.Any(x => string.Equals(x, SelectedLayout, StringComparison.OrdinalIgnoreCase)))
                        {
                            SelectedLayout = null;
                            ActionRows.Clear();
                            OnPropertyChanged(nameof(HasActions));
                        }
                    }
                });
            };

            if (LayoutOptions is INotifyCollectionChanged ncc)
                ncc.CollectionChanged += (_, __) => BuildLayoutChoices();

            SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => CanSave());
            ClearCommand = new RelayCommand(_ => Clear(), _ => CanClear());
        }

        // ── Build layout list and choices (with a real blank first item) ─────────
        private void RebuildLayoutOptions()
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _byDevice.Values)
                if (!string.IsNullOrWhiteSpace(kv.Layout))
                    set.Add(kv.Layout);

            var desired = set.ToList(); // already sorted
            if (LayoutOptions.SequenceEqual(desired, StringComparer.OrdinalIgnoreCase))
            {
                BuildLayoutChoices();
                return;
            }

            LayoutOptions.Clear();
            foreach (var s in desired) LayoutOptions.Add(s);
            BuildLayoutChoices();
        }

        private void BuildLayoutChoices()
        {
            var keep = SelectedLayout;

            LayoutChoices.Clear();
            LayoutChoices.Add(new Choice<string?>(null, " ")); // blank option
            foreach (var l in LayoutOptions)
                LayoutChoices.Add(new Choice<string?>(l, l));

            // keep selection if still valid
            SelectedLayout = keep != null && LayoutOptions.Contains(keep) ? keep : null;
        }

        // ── Populate the actions for current layout (top-down, unique) ───────────
        private async Task PopulateRowsForSelectedLayoutAsync()
        {
            ActionRows.Clear();
            if (string.IsNullOrWhiteSpace(SelectedLayout))
            {
                OnPropertyChanged(nameof(HasActions));
                return;
            }

            var seq = _byDevice.Values
                               .Where(v => string.Equals(v.Layout, SelectedLayout, StringComparison.OrdinalIgnoreCase))
                               .SelectMany(v => v.Actions ?? Array.Empty<string>())
                               .Where(a => !string.IsNullOrWhiteSpace(a));

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in seq)
                if (seen.Add(a)) ActionRows.Add(new ActionRow(a));

            OnPropertyChanged(nameof(HasActions));
            await Task.CompletedTask;
        }

        // ── Save (validate, block overwrite, persist, refresh catalog, associate) ─
        private static readonly Regex InvalidNameChars =
            new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]",
                RegexOptions.Compiled);

        private bool CanSave()
        {
            if (string.IsNullOrWhiteSpace(SelectedLayout)) return false;
            if (string.IsNullOrWhiteSpace(FileName)) return false;
            if (InvalidNameChars.IsMatch(FileName!)) return false;
            if (ActionRows.Count == 0) return false;
            if (ActionRows.Any(r => string.IsNullOrWhiteSpace(r.KeyBinding))) return false;
            return true;
        }

        private void RaiseCanSave() => CommandManager.InvalidateRequerySuggested();

        private async Task SaveAsync()
        {
            if (!CanSave()) return;

            var nameNoExt = Path.GetFileNameWithoutExtension(FileName!.Trim());

            // Block overwrite
            var existing = await _store.ListAsync();
            if (existing.Any(x => string.Equals(x, nameNoExt, StringComparison.OrdinalIgnoreCase)))
            {
                _msg.Warn($"A mapping named '{nameNoExt}' already exists. Please choose a different name.");
                return;
            }

            // Persist
            var dto = new InputMappingFile
            {
                Version = 1,
                Layout = SelectedLayout!,
                Bindings = ActionRows.ToDictionary(r => r.ActionName, r => r.KeyBinding ?? "",
                                                   StringComparer.OrdinalIgnoreCase)
            };

            await _store.SaveAsync(nameNoExt, dto);

            // Refresh catalog + association
            _mappings.Refresh();
            await _assoc.AddAssociationAsync(SelectedLayout!, nameNoExt);

            _msg.Info($"Mapping '{nameNoExt}' created and associated with layout '{SelectedLayout}'.");

            // reset form
            FileName = "";
            foreach (var r in ActionRows) r.KeyBinding = "";

            // 🔑 reset layout selection → ComboBox goes to blank
            SelectedLayout = null;
            ActionRows.Clear();
            OnPropertyChanged(nameof(HasActions));

            RaiseCanSave();
        }

        // Clear view
        private bool CanClear()
        {
            // Enable when something is filled
            return !string.IsNullOrWhiteSpace(FileName)
                   || ActionRows.Any(r => !string.IsNullOrWhiteSpace(r.KeyBinding));
        }

        private void Clear()
        {
            // reset form
            FileName = string.Empty;
            foreach (var r in ActionRows)
                r.KeyBinding = string.Empty;

            // if you already have this helper, keep it:
            RaiseCanSave();

            // also refresh Clear button’s CanExecute (and others that use CommandManager)
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }


        // Row VM used in the actions box.
        public sealed class ActionRow : ObservableObjects
        {
            public string ActionName { get; }
            private string _key = "";
            public string KeyBinding
            {
                get => _key;
                set { if (_key == value) return; _key = value; OnPropertyChanged(); }
            }
            public ActionRow(string action) => ActionName = action;
        }
    }
}