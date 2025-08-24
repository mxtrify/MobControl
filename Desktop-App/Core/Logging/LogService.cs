using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace MobControlUI.Core.Logging
{
    public sealed class LogService : ILogService
    {
        public static LogService Instance { get; } = new();

        public ObservableCollection<LogEntry> Entries { get; } = new();

        private Dispatcher? UIDispatcher => Application.Current?.Dispatcher;

        public void Add(string message, string level = "Info")
        {
            void Do() => Entries.Add(new LogEntry(message, level));
            if (UIDispatcher is { } d)
            {
                if (d.CheckAccess()) Do();
                else d.BeginInvoke((Action)Do);
            }
            else
            {
                // Fallback (e.g., during very early startup)
                Do();
            }
        }

        public void Clear()
        {
            void Do() => Entries.Clear();
            if (UIDispatcher is { } d)
            {
                if (d.CheckAccess()) Do();
                else d.BeginInvoke((Action)Do);
            }
            else Do();
        }
    }
}
