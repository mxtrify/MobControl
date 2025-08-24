using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MobControlUI.Core.Logging
{
    public sealed class LogEntry
    {
        public LogEntry(string message, string level = "Info")
        {
            Timestamp = DateTime.Now;
            Message = message;
            Level = level;
        }
        public DateTime Timestamp { get; }
        public string Message { get; }
        public string Level { get; }
        public string Display => $"[{Timestamp:HH:mm:ss}] {Message}";
    }
}
