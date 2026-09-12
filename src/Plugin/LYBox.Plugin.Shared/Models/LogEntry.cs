using CommunityToolkit.Mvvm.ComponentModel;

namespace LYBox.Plugin.Shared.Models;

/// <summary>
/// 日志条目（共享 UILogger 组件的数据模型）。
/// 普通日志只填充 <see cref="Message"/>；进度类日志设置 <see cref="IsProgress"/>
/// 并通过 <see cref="Services.DirectLogger"/> 更新进度属性。
/// </summary>
public partial class LogEntry : ObservableObject
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Message { get; init; } = string.Empty;
    public string FormattedLine => $"[{Timestamp:HH:mm:ss}] {Message}";

    public bool IsProgress { get; init; }
    public string FileName { get; init; } = string.Empty;
    public long FileSize { get; init; }

    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>复制到剪贴板时使用的文本：进度行输出 文件名 + 状态 + 百分比，普通行输出带时间戳的整行。</summary>
    public string CopyText => IsProgress
        ? $"{FileName} - {StatusText} ({ProgressValue:F1}%)"
        : FormattedLine;
}
