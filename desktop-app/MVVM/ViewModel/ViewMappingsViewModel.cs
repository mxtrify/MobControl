using MobControlUI.Core;
using MobControlUI.Core.Mapping;
using MobControlUI.Core.Storage;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace MobControlUI.MVVM.ViewModel
{
    public sealed class ViewMappingsViewModel : ObservableObjects
    {
        private readonly IMappingCatalog _catalog;

        // Bind your list to this (names as provided by the catalog)
        public ReadOnlyObservableCollection<string> MappingNames => _catalog.Names;

        // For an empty-state message in XAML if you want it
        public bool HasMappings => MappingNames.Count > 0;

        // “>” button
        public RelayCommand EditCommand { get; }

        // Bubble up so MainViewModel can navigate to UpdateMappingView
        public event Action<string>? EditRequested;

        public ViewMappingsViewModel(IMappingCatalog catalog)
        {
            _catalog = catalog;

            // ensure the catalog is populating Names and watching the folder
            _catalog.Start();

            // keep HasMappings in sync when files appear/disappear
            if (_catalog.Names is INotifyCollectionChanged ncc)
            {
                ncc.CollectionChanged += (_, __) => OnPropertyChanged(nameof(HasMappings));
            }

            EditCommand = new RelayCommand(p =>
            {
                var name = p as string;
                if (!string.IsNullOrWhiteSpace(name))
                    EditRequested?.Invoke(name);
            });
        }
    }
}