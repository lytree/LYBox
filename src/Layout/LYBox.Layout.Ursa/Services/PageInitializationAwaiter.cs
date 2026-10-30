using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using LYBox.Plugin.Shared;

namespace LYBox.Layout.Ursa.Services;

/// <summary>
/// 等待页面（或其 ViewModel）声明的 <see cref="IPageInitialization.InitializationTask"/> 完成，
/// 用于在关闭加载遮罩前确认首屏数据已就绪。
/// </summary>
internal static class PageInitializationAwaiter
{
    /// <exception cref="InvalidOperationException">
    /// 实现方把 <c>InitializationTask</c> 赋值为 <c>null</c>，属于契约违约。
    /// </exception>
    internal static async Task WaitAsync(CancellationToken cancellationToken, params object?[] owners)
    {
        // 页面与其 ViewModel 可能暴露同一个任务，用引用相等去重
        var observed = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pending = new List<Task>();
            foreach (var owner in owners)
            {
                if (GetInitializationTask(owner) is { } task && observed.Add(task))
                    pending.Add(task);
            }

            if (pending.Count == 0)
                return;

            // 失败的任务不能被当作成功；切换页面时也不取消缓存页面自身的加载。
            // 后续再次进入该页面时会继续等待同一个任务。
            await Task.WhenAll(pending).WaitAsync(cancellationToken);
        }
    }

    [SuppressMessage("Trimming", "IL2075", Justification = "插件页面类型在已加载程序集中，反射属性固定且存在性由兼容契约保证。")]
    private static Task? GetInitializationTask(object? owner)
    {
        if (owner is IPageInitialization initialization)
            return initialization.InitializationTask
                ?? throw new InvalidOperationException("InitializationTask 不能为 null。");

        if (owner is null)
            return null;

        // 兼容旧版 SDK 的插件：只要求公开 Task InitializationTask 属性，无需重新引用新包。
        var property = owner.GetType().GetProperty(
            nameof(IPageInitialization.InitializationTask), BindingFlags.Instance | BindingFlags.Public);
        if (property is null || property.GetIndexParameters().Length != 0
            || !typeof(Task).IsAssignableFrom(property.PropertyType))
            return null;

        return property.GetValue(owner) as Task
            ?? throw new InvalidOperationException("InitializationTask 不能为 null。");
    }
}
