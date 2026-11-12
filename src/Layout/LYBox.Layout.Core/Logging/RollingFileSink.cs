using System.Globalization;
using System.Text;
using LYBox.Plugin.Shared;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace LYBox.Layout.Core.Logging;

/// <summary>
/// 宿主自实现的滚动文件 sink：沿用既有日志文件命名
/// （<see cref="WellKnownPaths.FormatRollingLogFileName"/> → <c>app-yyyy-MM-dd_NNN.log</c>），
/// 按 UTC 自然日切换文件，单文件超过 10MB 时顺延序号。
/// <para>
/// 每条事件写入后立即 flush，避免应用异常退出（<c>Environment.Exit</c>）时丢失日志；
/// 写入失败只关闭当前文件流，不影响业务调用方，下一条日志会重新尝试。
/// </para>
/// </summary>
internal sealed class RollingFileSink : ILogEventSink, IDisposable
{
    /// <summary>单文件大小上限，对齐既有 ZLogger RollingSizeKB = 10240（10MB）。</summary>
    private const long MaxFileSizeBytes = 10240L * 1024;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _logDirectory;
    private readonly ITextFormatter _formatter;
    private readonly object _sync = new();

    private StreamWriter? _writer;
    private DateTime _currentUtcDate;
    private int _sequence;

    public RollingFileSink(string logDirectory, ITextFormatter formatter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentNullException.ThrowIfNull(formatter);

        _logDirectory = logDirectory;
        _formatter = formatter;
    }

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        lock (_sync)
        {
            try
            {
                EnsureWriter(logEvent.Timestamp.UtcDateTime.Date);
                _formatter.Format(logEvent, _writer!);
                _writer!.Flush();
            }
            catch
            {
                // 日志落盘失败不应反过来打断业务；关闭当前流，下一条日志重新尝试。
                CloseWriter();
            }
        }
    }

    private void EnsureWriter(DateTime utcDate)
    {
        Directory.CreateDirectory(_logDirectory);

        var sizeExceeded = _writer is not null && _writer.BaseStream.Length >= MaxFileSizeBytes;

        if (_writer is not null && utcDate == _currentUtcDate && !sizeExceeded)
            return;

        if (_writer is not null && utcDate == _currentUtcDate)
        {
            // 同一天内超过单文件上限：顺延序号。
            CloseWriter();
            _sequence++;
        }
        else
        {
            // 跨天或首次写入：定位当天最后一个文件，已满则顺延。
            CloseWriter();
            _currentUtcDate = utcDate;
            _sequence = FindLatestSequence(utcDate);
            if (GetFileLength(utcDate, _sequence) >= MaxFileSizeBytes)
                _sequence++;
        }

        _writer = new StreamWriter(
            new FileStream(BuildPath(utcDate, _sequence), FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
            Utf8NoBom);
        _writer.AutoFlush = true;
    }

    private string BuildPath(DateTime utcDate, int sequence)
        => Path.Combine(
            _logDirectory,
            WellKnownPaths.FormatRollingLogFileName(new DateTimeOffset(utcDate, TimeSpan.Zero), sequence));

    private long GetFileLength(DateTime utcDate, int sequence)
    {
        var path = BuildPath(utcDate, sequence);
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    /// <summary>返回当天已存在的最大序号；没有历史文件时返回 0。</summary>
    private int FindLatestSequence(DateTime utcDate)
    {
        var prefix = $"{WellKnownPaths.RollingLogFilePrefix}-{utcDate:yyyy-MM-dd}_";
        var latest = 0;

        foreach (var path in Directory.EnumerateFiles(
                     _logDirectory,
                     prefix + "*" + WellKnownPaths.LogFileExtension))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Length <= prefix.Length)
                continue;

            var tail = name[prefix.Length..];
            if (int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
                latest = Math.Max(latest, sequence);
        }

        return latest;
    }

    private void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
            // Dispose 期间的 I/O 异常同样不应影响退出流程。
        }

        _writer = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            CloseWriter();
        }
    }
}
