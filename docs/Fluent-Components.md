# Fluent 组件：命令栏 / 顶部菜单栏 / 顶部导航 / 侧边导航视图

> 参照 `IzumiPL/Avalonia-Fluent-UI`（FluentAvalonia 衍生库）的组件语义，
> 使用本仓库现有的 Ursa 控件 + Fluent Design 样式实现，未引入新的 UI 依赖。
> 入口页面：侧边栏「控件示例」（Key = `FluentControls`）。

---

## 组件总览

| 组件 | 类型 | 来源 | 位置 |
|---|---|---|---|
| 命令栏 CommandBar | `FluentCommandBar` | 自绘（Button/ToggleButton 组合） | `Controls/FluentCommandBar.axaml` |
| 顶部菜单栏 MenuBar | `Menu` + `FluentMenuBar` 样式类 | Avalonia Menu + Ursa 菜单主题 | `Theme/FluentDesign/FluentDesignStyles.axaml` |
| 顶部导航 Top Navigation | `FluentTopNavigation` | 自绘（ItemsControl + 选中指示条 + 溢出菜单） | `Controls/FluentTopNavigation.axaml` |
| 侧边导航视图 NavigationView | `u:NavMenu` + `FluentNavPane` | Ursa NavMenu（宿主主壳同款） | `Views/MainView.axaml` / 示例页 |

> 说明：Ursa 2.2.0 未提供 `CommandBar` 控件（主题目录中的 `CommandBar.axaml` 仅为遗留资源），
> 因此命令栏按 WinUI CommandBar 语义自绘；菜单栏与侧边导航直接复用 Ursa 既有组件。

---

## 1. 命令栏 `FluentCommandBar`

图标 + 文字主命令、分隔线、切换按钮；命令数超过 `MaxVisibleItems` 时自动收进右侧「…」溢出浮出菜单。

```xml
<controls:FluentCommandBar
    ItemsSource="{Binding CommandBarItems}"
    LabelPosition="Right"
    MaxVisibleItems="7" />
```

| 属性 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `ItemsSource` | `IEnumerable<FluentCommandBarItem>` | — | 命令项集合 |
| `LabelPosition` | `FluentCommandLabelPosition` | `Right` | `Bottom` / `Right` / `Collapsed`（仅图标） |
| `MaxVisibleItems` | `int` | 8 | 超出部分进入溢出菜单 |
| `IsOverflowEnabled` | `bool` | `true` | 关闭后不收纳（横向滚动） |
| `HasOverflow` / `IsOverflowOpen` | `bool` | — | 只读状态 / 浮出菜单开关 |

数据模型 `FluentCommandBarItem`：`Header`、`IconName`（Fluent 图标资源键）、`Command`、`CommandParameter`、
`IsToggle`（渲染为 ToggleButton）、`IsChecked`、`IsSeparator`。

```csharp
new FluentCommandBarItem("保存", "FluentSave20Regular", SaveCommand),
new FluentCommandBarItem { IsSeparator = true },
new FluentCommandBarItem("编辑模式", "FluentEdit20Regular") { IsToggle = true, IsChecked = true },
```

---

## 2. 顶部菜单栏 `Menu.FluentMenuBar`

直接使用 Avalonia `Menu`，加 `FluentMenuBar` 样式类：透明背景、40px 行高、32px 菜单项与 4px 圆角。

```xml
<Menu Classes="FluentMenuBar">
    <MenuItem Header="文件">
        <MenuItem Header="新建" InputGesture="Ctrl+N" Command="{Binding NewCommand}" />
        <MenuItem Header="最近打开">
            <MenuItem Header="设计稿.psd" />
        </MenuItem>
        <Separator />
        <MenuItem Header="退出" />
    </MenuItem>
</Menu>
```

- 顶层项使用 Ursa `TopLevelMenuItem` 主题，子菜单自动使用 `MenuFlyout` 语义。
- `MenuItem.ToggleType="CheckBox" / "Radio"` 可做勾选态菜单项。

---

## 3. 顶部导航 `FluentTopNavigation`

水平导航条，选中项底部 2px accent 指示条；超过 `MaxVisibleItems` 的项收进「更多」浮出菜单。

```xml
<controls:FluentTopNavigation
    ItemsSource="{Binding TopNavigationItems}"
    MaxVisibleItems="5"
    SelectedItem="{Binding SelectedTopNavigationItem, Mode=TwoWay}" />
```

| 属性 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `ItemsSource` | `IEnumerable<FluentNavigationItem>` | — | 导航项集合 |
| `SelectedItem` | `FluentNavigationItem?` | — | 双向绑定 |
| `MaxVisibleItems` | `int` | 6 | 超出部分进入「更多」 |
| `IsOverflowEnabled` | `bool` | `true` | 是否启用溢出收纳 |
| `HasOverflow` / `IsOverflowOpen` | `bool` | — | 只读状态 / 浮出菜单开关 |

数据模型 `FluentNavigationItem`：`Header`、`Key`、`IconName`、`IsSelected`（控件维护）。

---

## 4. 侧边导航视图（`u:NavMenu`）

宿主主壳与示例页共用同一套：`Border.FluentNavPane` + `u:NavMenu` + 底部固定入口。

```xml
<Border Classes="FluentNavPane">
    <Grid RowDefinitions="Auto,*,Auto">
        <!-- 搜索 / 标题 -->
        <u:NavMenu
            Grid.Row="1"
            ItemsSource="{Binding SideNavigationItems}"
            SelectedItem="{Binding SelectedSideNavigationItem, Mode=TwoWay}"
            ExpandWidth="232" CollapseWidth="48"
            IsHorizontalCollapsed="{Binding IsSidePaneCollapsed}"
            HeaderBinding="{Binding MenuHeader}"
            IconBinding="{Binding MenuIconName}"
            SubMenuBinding="{Binding Children}" />
        <!-- 底部：设置 -->
    </Grid>
</Border>
```

- 子菜单：`MenuItemViewModel.Children` 非空时自动渲染 chevron 与浮出子项。
- 分组：`MenuItemViewModel.IsSeparator = true` 渲染分隔线（插件侧可用 `[Menu(..., Group = "...")]` 自动分组）。
- 折叠：`IsHorizontalCollapsed` 切换 48px 图标栏，展开宽度由 `ExpandWidth` 控制。

---

## 已知边界

- `FluentTopNavigation` / `FluentCommandBar` 的溢出判定基于 `MaxVisibleItems` 计数，而非实际像素测量；
  窄窗口下如需精确贴边收纳，可后续接入 `SizeChanged` + 逐项测量。
- `FluentCommandBar` 的图标通过 `IconNameToPathConverter` 从应用资源解析，图标键需与本仓库 Fluent 图标集一致。
