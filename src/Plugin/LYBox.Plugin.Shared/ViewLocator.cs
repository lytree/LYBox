using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using LYBox.Plugin.Shared.Generated;

namespace LYBox.Plugin.Shared;

public class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, ViewFactory> _viewRegistry = new(100);
    // ConditionalWeakTable 使用 DependentHandle，即使 Value(Key) 形成循环引用，
    // GC 仍能识别并回收孤立的对象对，解决 ViewModel→View→DataContext→ViewModel 循环引用导致的内存泄漏。
    private static ConditionalWeakTable<object, Control> _viewCache = new();

    public static void Register<TViewModel, TView>()
        where TView : Control, new()
    {
        _viewRegistry[typeof(TViewModel)] = () => new TView();
    }

    /// <summary>
    /// 注册源生成器模块描述的全部视图（唯一 UI 注册轨道：IGeneratedPluginModule.Ui）。
    /// 视图创建经 DI（GetService 优先，缺失时回退 new），services 须为宿主根容器。
    /// </summary>
    public static void RegisterModule(IGeneratedPluginModule module, IServiceProvider services)
    {
        foreach (var view in module.Ui.Views)
        {
            _viewRegistry[view.ViewModelType] = () => view.CreateView(services);
        }
    }

    public static void InvalidateViewCache(object viewModel)
    {
        _viewCache.Remove(viewModel);
    }

    public Control? Build(object? data)
    {
        if (data is null) return null;

        if (_viewCache.TryGetValue(data, out var cachedControl))
        {
            return cachedControl;
        }

        var type = data.GetType();

        if (_viewRegistry.TryGetValue(type, out var factory))
        {
            var control = factory();
            // 不显式设置 DataContext，由 Avalonia ContentControl 自动传播 DataContext。
            // 避免在 Build 中创建 View→ViewModel 强引用，让 ConditionalWeakTable 的
            // DependentHandle 机制能正确处理循环引用的 GC 回收。
            _viewCache.Add(data, control);
            return control;
        }

        return new TextBlock
        {
            Text = $"View not found for: {type.FullName}. \nPlease ensure it is registered via [ViewMap] (IGeneratedPluginModule.Ui).",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
    }

    public bool Match(object? data) => data is not null;
}
