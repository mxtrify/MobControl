using System.Windows;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using MobControlUI.Core;

namespace MobControlUI.MVVM.ViewModel
{
    public sealed class MainViewModel : ObservableObjects
    {
        private readonly IServiceProvider _sp;

        public ICommand HomeViewCommand { get; }
        public ICommand ControllerViewCommand { get; }
        public ICommand CreateMappingViewCommand { get; }
        public ICommand ViewMappingsViewCommand { get; }
        public ICommand LogsViewCommand { get; }

        public HomeViewModel HomeVM { get; }
        public ControllerViewModel ControllerVM { get; }
        public CreateMappingViewModel CreateMappingVM { get; }
        public ViewMappingsViewModel ViewMappingsVM { get; }
        public LogsViewModel LogsVM { get; }

        private object? _currentView;
        public object? CurrentView
        {
            get => _currentView;
            set
            {
                if (!Equals(_currentView, value))
                {
                    _currentView = value;
                    OnPropertyChanged();
                }
            }
        }

        public MainViewModel(
            IServiceProvider sp,
            HomeViewModel homeVM,
            ControllerViewModel controllerVM,
            CreateMappingViewModel createMappingVM,
            ViewMappingsViewModel viewMappingsVM,
            LogsViewModel logsVM)
        {
            _sp = sp;

            HomeVM = homeVM;
            ControllerVM = controllerVM;
            CreateMappingVM = createMappingVM;
            ViewMappingsVM = viewMappingsVM;
            LogsVM = logsVM;

            // basic nav
            HomeViewCommand = new RelayCommand(_ => CurrentView = HomeVM);
            ControllerViewCommand = new RelayCommand(_ => CurrentView = ControllerVM);
            CreateMappingViewCommand = new RelayCommand(_ => CurrentView = CreateMappingVM);
            ViewMappingsViewCommand = new RelayCommand(_ => CurrentView = ViewMappingsVM);
            LogsViewCommand = new RelayCommand(_ => CurrentView = LogsVM);

            // when user clicks ">" in ViewMappings, open UpdateMappingView
            ViewMappingsVM.EditRequested += async name => await OpenUpdateMappingAsync(name);

            CurrentView = HomeVM; // default
        }

        public void NavigateToViewMappings()
        {
            CurrentView = ViewMappingsVM;
        }


        private async Task OpenUpdateMappingAsync(string mappingName)
        {
            // Resolve a fresh editor VM each time (scoped/singleton per your DI)
            var editor = _sp.GetRequiredService<UpdateMappingViewModel>();

            // If your UpdateMappingViewModel exposes async loader, call it.
            // Safe if it’s sync: just remove await.
            await editor.LoadAsync(mappingName);

            // Ensure the editor reflects the newest actions before showing it
            // (must run on UI thread because it touches ObservableCollection)
            Application.Current?.Dispatcher.Invoke(() => editor.RefreshAgainstLiveLayout());

            CurrentView = editor;
        }
    }
}