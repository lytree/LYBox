namespace LYBox.Plugin.Shared;

/// <summary>
/// 暴露页面初始数据就绪的任务。页面或其 ViewModel 均可实现本接口。
/// </summary>
public interface IPageInitialization
{
    /// <summary>
    /// 在初始查询与绑定集合更新完成后完成。
    /// 请在构造函数或 Loaded 处理器中同步赋值，且不要在 getter 中启动工作；
    /// 多个独立加载请使用 Task.WhenAll 合并。缓存页面应返回同一个任务实例。
    /// 导航取消只会停止等待，不会取消该任务。
    /// </summary>
    Task InitializationTask { get; }
}
