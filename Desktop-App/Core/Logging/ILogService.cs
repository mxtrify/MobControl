using System.Collections.ObjectModel;

namespace MobControlUI.Core.Logging
{
    public interface ILogService
    {
        ObservableCollection<LogEntry> Entries { get; }
        void Add(string message, string level = "Info");
        void Clear();
    }
}
