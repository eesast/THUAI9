using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace THUAI9_Avalonia.ViewModels
{
    /// <summary>
    /// 单条日志记录。
    /// </summary>
    public partial class LogEntry : ObservableObject
    {
        [ObservableProperty]
        private string message = string.Empty;

        [ObservableProperty]
        private string timestamp = string.Empty;

        [ObservableProperty]
        private string level = "INFO";

        public LogEntry()
        {
            Timestamp = System.DateTime.Now.ToString("HH:mm:ss.fff");
        }

        public LogEntry(string message, string level = "INFO") : this()
        {
            Message = message;
            Level = level;
        }
    }

    /// <summary>
    /// 日志控制台视图模型。
    /// </summary>
    public partial class LogConsoleViewModel : ViewModelBase
    {
        private const int MaxLogEntries = 1000;

        [ObservableProperty]
        private ObservableCollection<LogEntry> logEntries = new();

        public void AddLog(string message, string level = "INFO")
        {
            var entry = new LogEntry(message, level);

            if (LogEntries.Count >= MaxLogEntries)
            {
                LogEntries.RemoveAt(0);
            }

            LogEntries.Add(entry);
            OnPropertyChanged(nameof(LogEntries));
        }

        public void Clear()
        {
            LogEntries.Clear();
            OnPropertyChanged(nameof(LogEntries));
        }
    }
}
