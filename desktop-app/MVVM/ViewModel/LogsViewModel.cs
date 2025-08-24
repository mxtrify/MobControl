using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using MobControlUI.Core;
using MobControlUI.Core.Logging;

namespace MobControlUI.MVVM.ViewModel
{
    public class LogsViewModel : ObservableObjects
    {
        private readonly ILogService _log;

        public ObservableCollection<LogEntry> Entries => _log.Entries;

        public ICommand ClearCommand { get; }

        public LogsViewModel(ILogService log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            ClearCommand = new RelayCommand(_ => _log.Clear());
        }
    }
}
