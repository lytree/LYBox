using LYBox.Plugin.Shared.Models;

namespace LYBox.Plugin.Shared.Services;

/// <summary>
/// 直连日志器：把日志与进度事件直接回调到 UI 层（由 ViewModel 决定如何写入集合）。
/// 普通日志走 <see cref="Log"/>；下载/传输类进度走 Start/Update/Complete/Fail 系列。
/// </summary>
public class DirectLogger
{
    private readonly Action<string> _onLog;
    private readonly Action<LogEntry> _onAddEntry;
    private readonly Action<LogEntry, double, string, bool, bool> _onUpdateProgress;

    public DirectLogger(
        Action<string> onLog,
        Action<LogEntry>? onAddEntry = null,
        Action<LogEntry, double, string, bool, bool>? onUpdateProgress = null)
    {
        _onLog = onLog;
        _onAddEntry = onAddEntry ?? (_ => { });
        _onUpdateProgress = onUpdateProgress ?? ((_, _, _, _, _) => { });
    }

    public void Log(string message) => _onLog(message);

    public LogEntry StartProgress(string fileName, long fileSize, string initialStatus)
    {
        var entry = new LogEntry
        {
            IsProgress = true,
            FileName = fileName,
            FileSize = fileSize,
            StatusText = initialStatus
        };
        _onAddEntry(entry);
        return entry;
    }

    public void UpdateProgress(LogEntry entry, double progressValue, string status)
    {
        _onUpdateProgress(entry, progressValue, status, false, false);
    }

    public void CompleteProgress(LogEntry entry, string status)
    {
        _onUpdateProgress(entry, 100, status, true, false);
    }

    public void FailProgress(LogEntry entry, string status)
    {
        _onUpdateProgress(entry, entry.ProgressValue, status, false, true);
    }
}
