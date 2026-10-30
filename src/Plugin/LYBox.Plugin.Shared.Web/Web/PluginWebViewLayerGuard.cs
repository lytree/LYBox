using Avalonia.Threading;

namespace LYBox.Plugin.Shared.Web;

/// <summary>
/// 原生层 WebView 的显示抑制开关：宿主弹出 Overlay 对话框/遮罩时，
/// 通过 <see cref="Suppress"/> 临时摘除 WebView，避免原生子窗口盖住 Avalonia 浮层。
/// 引用计数支持嵌套弹窗；<see cref="ISuppressionScope.Dispose"/> 后自动恢复。
/// </summary>
public static class PluginWebViewLayerGuard
{
    private static int s_suppressionCount;

    /// <summary>抑制状态发生变化（进入或退出抑制）时在 UI 线程触发。</summary>
    public static event EventHandler? SuppressionChanged;

    /// <summary>当前是否处于抑制状态。</summary>
    public static bool IsSuppressed => Volatile.Read(ref s_suppressionCount) > 0;

    /// <summary>进入抑制状态，释放返回的作用域即退出。</summary>
    public static ISuppressionScope Suppress()
    {
        if (Interlocked.Increment(ref s_suppressionCount) == 1)
            RaiseSuppressionChanged();

        return new SuppressionScope();
    }

    private static void Release()
    {
        var count = Interlocked.Decrement(ref s_suppressionCount);
        if (count == 0)
            RaiseSuppressionChanged();
    }

    private static void RaiseSuppressionChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            SuppressionChanged?.Invoke(null, EventArgs.Empty);
            return;
        }

        Dispatcher.UIThread.Post(static () => SuppressionChanged?.Invoke(null, EventArgs.Empty));
    }

    /// <summary>抑制作用域；可重复 Dispose。</summary>
    public interface ISuppressionScope : IDisposable
    {
    }

    private sealed class SuppressionScope : ISuppressionScope
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Release();
        }
    }
}
