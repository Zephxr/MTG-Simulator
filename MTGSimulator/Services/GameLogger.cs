using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGSimulator.Services
{
    public class GameLogger
    {
        public class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public string Message { get; set; } = "";
        }

        private readonly List<LogEntry> _logs = new List<LogEntry>();
        private const int MaxLogEntries = 500;

        public event Action<LogEntry>? LogAdded;

        public void Log(string message)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = message
            };

            _logs.Add(entry);

            // Limit log size
            if (_logs.Count > MaxLogEntries)
            {
                _logs.RemoveAt(0);
            }

            LogAdded?.Invoke(entry);
        }

        public List<LogEntry> GetRecentLogs(int count = 100)
        {
            return _logs.TakeLast(count).ToList();
        }

        public string GetLogText(int maxEntries = 100)
        {
            var recentLogs = GetRecentLogs(maxEntries);
            return string.Join("\n", recentLogs.Select(log => $"[{log.Timestamp:HH:mm:ss}] {log.Message}"));
        }

        public void Clear()
        {
            _logs.Clear();
        }
    }
}

