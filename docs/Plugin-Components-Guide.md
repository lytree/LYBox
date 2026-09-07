# 插件可用组件列表与使用指南
本指南面向插件开发者，列出当前 Host 项目暴露给所有插件的可复用 UI 组件、样式类、服务与基础设施，并给出推荐用法与示例。所有组件均通过 `LYBox.Plugin.Shared` 与 Host 主题系统自动注入，无需插件主动引用额外 NuGet 包。

---

## 目录

- [1. 组件选型优先级](#1-组件选型优先级)
- [2. Ursa 控件清单（首选）](#2-ursa-控件清单首选)
- [3. Avalonia 内置控件](#3-avalonia-内置控件)
- [4. 项目自定义 Fluent 样式类](#4-项目自定义-fluent-样式类)
  - [4.1 新增样式类（本轮新增）](#41-新增样式类本轮新增)
  - [4.2 卡片 / 容器样式](#42-卡片--容器样式)
  - [4.3 按钮样式](#43-按钮样式)
  - [4.4 文本 / 排版样式](#44-文本--排版样式)
  - [4.5 设置卡片样式](#45-设置卡片样式)
  - [4.6 信息条 / 徽章 / 进度](#46-信息条--徽章--进度)
  - [4.7 面包屑 / 对话框](#47-面包屑--对话框)
  - [4.8 已弃用样式（禁止使用）](#48-已弃用样式禁止使用)
- [5. 颜色与画刷资源](#5-颜色与画刷资源)
- [6. 图标资源](#6-图标资源)
- [7. 服务定位器 ServiceLocator](#7-服务定位器-servicelocator)
- [8. ViewLocator 自动视图映射](#8-viewlocator-自动视图映射)
- [9. 完整插件页面示例](#9-完整插件页面示例)

---

## 1. 组件选型优先级

| 优先级 | 来源 | 命名空间 | 适用场景 |
|--------|------|----------|---------|
| 1 | **Irihi.Ursa** | `xmlns:u="https://irihi.tech/ursa"` | 默认首选。所有通用控件优先用 Ursa。 |
| 2 | **Avalonia 内置控件** | `xmlns="https://github.com/avaloniaui"` | Ursa 未覆盖或场景不适合时使用。 |
| 3 | **项目自定义 Fluent 补充样式** | `Classes="Fluent..."` | Ursa 未提供的 WinUI 风格控件。 |
| 4 | **CommunityToolkit.Mvvm** | `using CommunityToolkit.Mvvm.ComponentModel;` | ViewModel 基础设施（VM 必须用此库）。 |

**禁止**：直接引入 `Avalonia-Fluent-UI`（`AvaloniaFluentUI`）整库。需要 WinUI 风格控件时使用项目自定义补充样式。

---

## 2. Ursa 控件清单（首选）

插件通过 `LYBox.Plugin.Shared` 的程序集转发机制可访问所有 Ursa 控件。在 XAML 顶部添加：
```xml
xmlns:u="https://irihi.tech/ursa"
```

### 通用控件

| 控件 | XAML | 用途 |
|------|------|------|
| `Button` | `<u:Button>` | 主按钮（替代 Avalonia `Button`） |
| `IconButton` | `<u:IconButton Icon="..." />` | 纯图标按钮 |
| `Banner` | `<u:Banner />` | 顶部横幅通知 |
| `NavMenu` / `NavMenuItem` | `<u:NavMenu>` | 侧栏导航菜单 |
| `Form` | `<u:Form>` | 表单布局 |
| `NumericUpDown` | `<u:NumericUpDown />` | 数值输入框 |
| `TagInput` | `<u:TagInput />` | 标签输入 |
| `IPv4Box` | `<u:IPv4Box />` | IPv4 输入 |
| `TimeBox` | `<u:TimeBox />` | 时间输入 |
| `Avatar` | `<u:Avatar />` | 头像 |
| `Card` | `<u:Card />` | 卡片容器 |
| `Badge` | `<u:Badge />` | 徽章 |
| `Loading` | `<u:Loading />` | 加载指示 |
| `Breadcrumb` | `<u:Breadcrumb />` | 面包屑 |
| `Dialog` | 通过 `DialogContext` 调用 | 模态对话框 |
| `Drawer` | 通过 `DrawerContext` 调用 | 抽屉 |
| `ToggleSwitch` | `<u:ToggleSwitch />` | 开关 |
| `Toast` | `ToastService.Show(...)` | 轻提示 |

### Ursa 命名空间分组

| 前缀 | 命名空间 | 包含 |
|------|---------|------|
| `u:` | `Irihi.Ursa.Controls` | Button, Banner, NavMenu, Form, NumericUpDown, TagInput 等 |
| `u:` | `Irihi.Ursa.Controls.OverlayHost` | Dialog/Drawer 宿主（自动注入） |

---

## 3. Avalonia 内置控件

无需 xmlns 引入，直接使用。已应用 `DataGridFluentTheme`。

| 控件 | 用途 |
|------|------|
| `Button`, `TextBox`, `CheckBox`, `ComboBox` | 基础表单控件 |
| `ListBox`, `TreeView` | 列表/树形展示 |
| `TabControl` | 选项卡 |
| `ProgressBar`, `Slider` | 进度/滑块 |
| `DatePicker`, `TimePicker` | 日期时间选择 |
| `DataGrid` | 数据表格（已应用 Fluent 主题） |
| `Expander` | 折叠面板 |
| `Menu`, `ContextMenu` | 菜单 |
| `ScrollViewer` | 滚动容器 |
| `PathIcon`, `Image` | 图标 |

---

## 4. 项目自定义 Fluent 样式类

文件位置：[`src/Layout/LYBox.Layout.Ursa/Theme/FluentDesign/FluentDesignStyles.axaml`](../src/Layout/LYBox.Layout.Ursa/Theme/FluentDesign/FluentDesignStyles.axaml)

通过 `UrsaSemiTheme` 自动加载，**无需手动 `<StyleInclude>`**。在任意控件上通过 `Classes="类名"` 使用。

### 4.1 新增样式类（本轮新增）

#### FluentHyperlinkButton（P1）

WinUI HyperlinkButton 风格，透明背景 + Accent 色前景 + 悬停下划线 + 焦点边框。

```xml
<!-- 基础用法 -->
<Button Classes="FluentHyperlinkButton" Command="{Binding OpenLinkCommand}">
    查看文档
</Button>

<!-- 在文本流中作为链接 -->
<TextBlock>
    请阅读
    <Button Classes="FluentHyperlinkButton" Command="{Binding ShowTermsCommand}">使用条款</Button>
    后继续。
</TextBlock>
```

| 状态 | 行为 |
|------|------|
| 默认 | Accent 色，无下划线 |
| `:pointerover` | Accent pointerover 色 + 下划线 |
| `:pressed` | Accent pressed 色 |
| `:focus` | Accent 色细边框 |
| `:disabled` | 灰色前景 |

与 `FluentHypertext` 区别：`FluentHyperlinkButton` 是 WinUI 官方命名，包含 focus 边框，更适合命令式按钮；`FluentHypertext` 适合纯文字按钮。

---

#### FluentInfoBar（P2）

WinUI InfoBar 风格 - 含严重级别左边框 + 浅色背景 + 关闭按钮。

```xml
<Border Classes="FluentInfoBar FluentInfoBarSeverityInformational">
    <Grid ColumnDefinitions="Auto,*,Auto">
        <PathIcon Grid.Column="0"
                  Data="{DynamicResource FluentIcon20FilledInfo}"
                  Width="20" Height="20"
                  Foreground="{DynamicResource FluentAccentBrush}" />
        <StackPanel Grid.Column="1" Margin="12,0">
            <TextBlock Classes="FluentInfoBarTitle" Text="提示" />
            <TextBlock Classes="FluentInfoBarMessage" Text="这是一条信息提示内容。" TextWrapping="Wrap" />
        </StackPanel>
        <Button Grid.Column="2"
                Classes="FluentInfoBarCloseButton"
                Command="{Binding DismissCommand}">
            <PathIcon Data="{DynamicResource FluentDismiss16Regular}" Width="12" Height="12" />
        </Button>
    </Grid>
</Border>
```

**严重级别修饰类**：

| 修饰类 | 边框色 | 推荐图标 |
|--------|--------|---------|
| `FluentInfoBarSeverityInformational` | Accent 蓝 | `FluentIcon20FilledInfo` |
| `FluentInfoBarSeverityWarning` | 橙黄 | `FluentIcon20FilledWarning` |
| `FluentInfoBarSeveritySuccess` | 绿 | `FluentIcon20FilledCheckmarkCircle` |
| `FluentInfoBarSeverityError` | 红 | `FluentIcon20FilledErrorCircle` |

**配套类**：`FluentInfoBarTitle`（标题）、`FluentInfoBarMessage`（正文）、`FluentInfoBarCloseButton`（关闭按钮）。

---

#### FluentSegmentedItem（P3）

WinUI Segmented 控件项样式 - 通过 `ListBox` + `ListBoxItem` 实现。

```xml
<ListBox Classes="FluentSegmentedControl"
         SelectedIndex="{Binding SelectedViewMode}"
         Background="Transparent">
    <ListBoxItem Classes="FluentSegmentedItem">
        <StackPanel Orientation="Horizontal" Spacing="8">
            <PathIcon Data="{DynamicResource FluentIcon16RegularList}" Width="16" Height="16" />
            <TextBlock Text="列表" />
        </StackPanel>
    </ListBoxItem>
    <ListBoxItem Classes="FluentSegmentedItem">
        <StackPanel Orientation="Horizontal" Spacing="8">
            <PathIcon Data="{DynamicResource FluentIcon16RegularGrid}" Width="16" Height="16" />
            <TextBlock Text="网格" />
        </StackPanel>
    </ListBoxItem>
    <ListBoxItem Classes="FluentSegmentedItem">
        <TextBlock Text="详细信息" />
    </ListBoxItem>
</ListBox>
```

| 类名 | 用途 |
|------|------|
| `FluentSegmentedControl` | 容器：横向 StackPanel + 圆角外框 |
| `FluentSegmentedItem` | 项：选中态 Accent 文字 + 加粗 + 白色背景 |

---

#### FluentSettingsExpander（P3）

WinUI SettingsExpander 风格 - 折叠式设置卡片。

```xml
<Expander Classes="FluentSettingsExpander">
    <Expander.Header>
        <Grid ColumnDefinitions="Auto,*,Auto">
            <Border Grid.Column="0" Classes="FluentSettingsCardIconHost">
                <PathIcon Data="{DynamicResource FluentIcon24RegularSettings}"
                          Width="20" Height="20" />
            </Border>
            <StackPanel Grid.Column="1" Margin="12,0" VerticalAlignment="Center">
                <TextBlock Classes="FluentSettingsCardTitle" Text="外观" />
                <TextBlock Classes="FluentSettingsCardDescription" Text="配置主题、强调色" />
            </StackPanel>
        </Grid>
    </Expander.Header>
    <Expander.Content>
        <StackPanel Spacing="12">
            <TextBlock Classes="WinUIBody" Text="展开后的内容..." />
            <u:ToggleSwitch IsChecked="{Binding EnableDarkMode}" />
        </StackPanel>
    </Expander.Content>
</Expander>
```

| 行为 | 说明 |
|------|------|
| 默认 | 1px 边框 + 8px 圆角 + 卡片背景 |
| `:pointerover` | 边框变 Accent 色 |
| Header | 16,12 内边距 + 最小高度 56 |
| Content | 16,12 内边距 + 顶部 1px 分隔线 |

---

### 4.2 卡片 / 容器样式

| 类名 | 控件 | 用途 |
|------|------|------|
| `WinUICard` | `Border` / `Button` | 基础卡片：1px 边框 + 4px 圆角 + 卡片背景 |
| `FluentElevatedCard` | `Border` | 抬升卡片：带阴影 + hover 阴影变化 |
| `FluentHeaderedCard` | `Border` | 带标题卡片：含 `FluentCardHeader` 子标题样式 |
| `FluentCardDivider` | `Border` | 卡片内分隔线 |
| `WinUINavPane` | `Border` | 导航栏背景 |
| `WinUITitleBar` | `Border` | 标题栏背景 |
| `FluentSmokeOverlay` | `Border` | 模态遮罩层 |

### 4.3 按钮样式

| 类名 | 用途 |
|------|------|
| `WinUIAccent` | Accent 色实心按钮 |
| `WinUISubtle` | 透明背景按钮 |
| `WinUINavItem` | 导航项按钮 |
| `WinUIHamburger` | 汉堡菜单按钮 |
| `WinUINavToggle` | 导航折叠按钮 |
| `FluentAccent` | Fluent Accent 实心按钮（带过渡动画） |
| `FluentStandard` | Fluent 标准按钮 |
| `FluentSubtle` | Fluent 透明背景按钮 |
| `FluentHypertext` | 文字链接按钮（与 `FluentHyperlinkButton` 类似） |
| `FluentSettingsCard` | 整个卡片可点击的设置项 |
| `FluentBreadcrumbItem` | 面包屑可点击项 |
| `FluentInfoBarCloseButton` | InfoBar 关闭按钮 |

### 4.4 文本 / 排版样式

| 类名 | 用途 |
|------|------|
| `WinUILargeTitle` | 大标题（28px SemiBold） |
| `WinUISectionHeader` | 段落标题（20px SemiBold） |
| `WinUISubtitle` | 副标题（16px SemiBold） |
| `WinUIBody` | 正文（14px） |
| `WinUICaption` | 说明文字（12px） |
| `WinUIPageHeader` | 页面页眉容器（StackPanel） |
| `WinUIPageDescription` | 页面描述文字 |

### 4.5 设置卡片样式

| 类名 | 控件 | 用途 |
|------|------|------|
| `FluentSettingsCard` | `Border` / `Button` | 设置页条目 |
| `FluentSettingsCardTitle` | `TextBlock` | 设置项标题 |
| `FluentSettingsCardDescription` | `TextBlock` | 设置项描述 |
| `FluentSettingsCardIconHost` | `Border` | 设置项图标容器 |
| `WinUISettingsCard` | `Border` | 基础设置卡片（旧版） |

### 4.6 信息条 / 徽章 / 进度

| 类名 | 用途 |
|------|------|
| `WinUIInfoBar` | 基础信息条 |
| `WinUIInfoBarWarning` / `Info` / `Success` / `Error` | 信息条严重级别 |
| `FluentInfoBadge` | 数字徽章基础类 |
| `FluentInfoBadgeCritical` / `Warning` / `Informational` / `Success` | 徽章严重级别 |
| `FluentInfoBadgeText` | 徽章文字 |
| `FluentInfoBadgeDot` | 点状徽章（Ellipse） |
| `FluentProgressRing` | 圆环进度（确定性） |
| `FluentProgressRing.Small` / `.Large` | 小/大圆环 |
| `circular` + `FluentProgressRing` + `IsIndeterminate="True"` | 不确定进度环 |

### 4.7 面包屑 / 对话框

| 类名 | 用途 |
|------|------|
| `FluentBreadcrumbItem` | 面包屑可点击项 |
| `FluentBreadcrumbCurrent` | 面包屑当前节点（不可点击） |
| `FluentBreadcrumbSeparator` | 面包屑分隔符 |
| `FluentContentDialogSurface` | 自定义对话框外层 |
| `FluentContentDialogTitle` | 对话框标题 |
| `FluentContentDialogBody` | 对话框正文 |
| `FluentContentDialogButtonRow` | 对话框按钮行 |
| `FluentNumeric` | Ursa NumericUpDown 的 Fluent 边框微调 |
| `FluentTagInput` | Ursa TagInput 的 Fluent 边框微调 |
| `FluentDivider` | 分隔线 |

### 4.8 已弃用样式（禁止使用）

以下样式源自 Semi.Avalonia，**不允许在新代码中使用**。新插件代码必须使用上表的 Fluent 替代项。

| 已弃用 | 推荐替代 |
|--------|---------|
| `Classes="H1"` / `H2` / `H3` / `H4` / `H5` | `Classes="WinUILargeTitle"` / `WinUISectionHeader` / `WinUISubtitle` / `WinUIBody` |
| `Classes="Secondary"` / `Tertiary` | `Classes="WinUICaption"` |
| `Classes="Primary"` | `Classes="FluentAccent"` 或 `WinUIAccent` |
| `Theme="{StaticResource TitleTextBlock}"` | 直接删除（`WinUILargeTitle` 等已包含字号） |
| `Theme="{DynamicResource CardBorder}"` | `Classes="WinUICard"` 或 `FluentElevatedCard` |
| `Theme="{DynamicResource SolidButton}"` | `Classes="FluentAccent"` |
| `Classes="SectionTitle"` | `Classes="WinUISectionHeader"` |

---

## 5. 颜色与画刷资源

### Fluent 语义资源（首选）

通过 `{DynamicResource 资源键}` 引用。

| 资源键 | 用途 |
|--------|------|
| `FluentAccentBrush` | Accent 主色 |
| `FluentAccentPointeroverBrush` | Accent 悬停色 |
| `FluentAccentPressedBrush` | Accent 按下色 |
| `FluentCardBackgroundBrush` | 卡片背景 |
| `FluentCardStrokeBrush` | 卡片边框 |
| `FluentSubtleBrush` | 微妙背景（次要区域） |
| `FluentSubtleHoverBrush` | 悬停态微妙背景 |
| `FluentSubtlePressedBrush` | 按下态微妙背景 |

### Semi 语义资源（次选 - 由 UrsaSemiTheme 自动适配）

| 资源键 | 用途 |
|--------|------|
| `SemiColorText0` / `Text1` / `Text2` | 主/次/弱文字色 |
| `SemiColorPrimary` | 主色 |
| `SemiColorDanger` / `Warning` / `Success` / `Info` | 语义色 |
| `SemiColorPrimaryLight` / `WarningLight` / `SuccessLight` / `DangerLight` | 浅色背景（InfoBar 用） |

**禁止**：在 XAML 中写死颜色字面量（如 `#FF0078D4`），必须使用上述资源。仅在阴影/Opacity mask 等无法用语义资源表达的场景允许使用字面量。

---

## 6. 图标资源

### Fluent Icons（首选）

文件位置：[`src/Layout/LYBox.Layout.Ursa/Theme/Icons/Fluent/`](../src/Layout/LYBox.Layout.Ursa/Theme/Icons/Fluent/)

命名规范：`FluentIcon{Size}{Variant}{Name}`

| Variant | 说明 |
|---------|------|
| `Regular` | 线框图标 |
| `Filled` | 实心图标 |

可用尺寸：16 / 20 / 24 / 28 / 32 / 48

```xml
<!-- PathIcon（首选，矢量） -->
<PathIcon Data="{DynamicResource FluentIcon24RegularSettings}" Width="20" Height="20" />

<!-- Image -->
<Image Source="{DynamicResource FluentIcon20FilledWarning}" Width="20" Height="20" />

<!-- 按钮内图标 -->
<Button Classes="FluentAccent">
    <PathIcon Data="{DynamicResource FluentIcon24RegularSave}" />
</Button>

<!-- IconButton（推荐用于纯图标按钮） -->
<u:IconButton Icon="{DynamicResource FluentIcon24RegularSettings}" />
```

### 常用图标速查

| 资源键 | 用途 |
|--------|------|
| `FluentIcon24RegularSettings` | 设置 |
| `FluentIcon20FilledWarning` | 警告 |
| `FluentIcon20FilledInfo` | 信息 |
| `FluentIcon20FilledCheckmarkCircle` | 成功 |
| `FluentIcon20FilledErrorCircle` | 错误 |
| `FluentIcon16RegularChevronDown` / `Up` / `Left` / `Right` | 箭头 |
| `FluentArrowMinimize16Regular` | 最小化 |
| `FluentMaximize16Regular` | 最大化 |
| `FluentDismiss16Regular` | 关闭/取消 |
| `FluentDismissCircle16Regular` | 圆形关闭 |

### 新增图标流程

1. 从 [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) 获取 SVG path
2. 转换为 `<StreamGeometry x:Key="FluentIcon{Size}{Variant}{Name}">path data</StreamGeometry>`
3. 追加到对应尺寸的 `Theme/Icons/Fluent/{Variant}{Size}.axaml`
4. 在 XAML 中以 `{DynamicResource FluentIcon...}` 引用

**禁止**：硬编码 `Geometry.Parse("...")` 字面量路径。

---

## 7. 服务定位器 ServiceLocator

静态 `IServiceProvider` 包装器，在插件代码中通过 `ServiceLocator.GetService<T>()` 获取 Host 注册的服务。

```csharp
using LYBox.Plugin.Shared;

// 强类型获取（不存在则抛异常）
var nav = ServiceLocator.GetService<INavigationService>();

// 安全获取
if (ServiceLocator.TryGetService<ILocalizationService>(out var loc))
{
    loc.RegisterResourceManager(Strings.ResourceManager);
}
```

**可用服务**（在 `App.Initialize()` 中注册）：

| 接口 | 命名空间 | 用途 |
|------|---------|------|
| `INavigationService` | `LYBox.Layout.Ursa.Services` | 页面导航 |
| `ILocalizationService` | `LYBox.Plugin.Shared.Services` | 多语言资源管理 |
| `ISettingsService` | `LYBox.Plugin.Shared.Services` | 设置读写 |
| `IPluginLoader` | `LYBox.Plugin.Shared.Services` | 插件加载器 |
| `IPluginInstallationManager` | `LYBox.Plugin.Shared.Services` | 插件安装管理 |
| `IWindowInfoService` | `LYBox.Plugin.Shared.Services` | 窗口信息 |
| `ITaskRegistry` | `LYBox.Plugin.Shared.Services` | 后台任务注册 |

调用前必须用 `TryGetService<T>` 检查存在性。

---

## 8. ViewLocator 自动视图映射

Host 注册的全局 `IDataTemplate`，自动按 ViewModel 类型名解析 View。约定：

| ViewModel | View |
|-----------|------|
| `FooViewModel` | `FooView` |
| `FooPageViewModel` | `FooPage` |

```xml
<!-- 在 ContentControl 中绑定 ViewModel，自动解析 View -->
<ContentControl Content="{Binding CurrentPage}" />
```

缓存通过 `ConditionalWeakTable` 实现（VM→View 循环无泄漏）。

---

## 9. 完整插件页面示例

结合本轮新增样式类的最佳实践示例：

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:u="https://irihi.tech/ursa"
             xmlns:vm="clr-namespace:MyPlugin.ViewModels"
             x:Class="MyPlugin.Pages.SettingsPage"
             x:DataType="vm:SettingsPageViewModel">

    <ScrollViewer Padding="24">
        <StackPanel Spacing="16" MaxWidth="800">

            <!-- 页面标题 -->
            <StackPanel Classes="WinUIPageHeader" Spacing="4">
                <TextBlock Classes="WinUILargeTitle" Text="插件设置" />
                <TextBlock Classes="WinUIPageDescription" Text="管理插件行为与外观" />
            </StackPanel>

            <!-- 信息提示（FluentInfoBar） -->
            <Border Classes="FluentInfoBar FluentInfoBarSeverityInformational"
                    IsVisible="{Binding HasUpdate}">
                <Grid ColumnDefinitions="Auto,*,Auto">
                    <PathIcon Grid.Column="0"
                              Data="{DynamicResource FluentIcon20FilledInfo}"
                              Width="20" Height="20"
                              Foreground="{DynamicResource FluentAccentBrush}" />
                    <StackPanel Grid.Column="1" Margin="12,0">
                        <TextBlock Classes="FluentInfoBarTitle" Text="有新版本可用" />
                        <TextBlock Classes="FluentInfoBarMessage"
                                   Text="v1.2.0 已发布，建议更新以获得最新功能。" />
                    </StackPanel>
                    <Button Grid.Column="2"
                            Classes="FluentInfoBarCloseButton"
                            Command="{Binding DismissUpdateCommand}">
                        <PathIcon Data="{DynamicResource FluentDismiss16Regular}"
                                  Width="12" Height="12" />
                    </Button>
                </Grid>
            </Border>

            <!-- 设置卡片（FluentSettingsExpander） -->
            <Expander Classes="FluentSettingsExpander">
                <Expander.Header>
                    <Grid ColumnDefinitions="Auto,*,Auto">
                        <Border Grid.Column="0" Classes="FluentSettingsCardIconHost">
                            <PathIcon Data="{DynamicResource FluentIcon24RegularSettings}"
                                      Width="20" Height="20" />
                        </Border>
                        <StackPanel Grid.Column="1" Margin="12,0" VerticalAlignment="Center">
                            <TextBlock Classes="FluentSettingsCardTitle" Text="外观" />
                            <TextBlock Classes="FluentSettingsCardDescription"
                                       Text="配置主题与显示模式" />
                        </StackPanel>
                    </Grid>
                </Expander.Header>
                <Expander.Content>
                    <StackPanel Spacing="12">
                        <Border Classes="FluentSettingsCard">
                            <Grid ColumnDefinitions="Auto,*,Auto">
                                <Border Grid.Column="0" Classes="FluentSettingsCardIconHost">
                                    <PathIcon Data="{DynamicResource FluentIcon24RegularDarkTheme}"
                                              Width="16" Height="16" />
                                </Border>
                                <StackPanel Grid.Column="1" Margin="12,0" VerticalAlignment="Center">
                                    <TextBlock Classes="FluentSettingsCardTitle" Text="深色模式" />
                                    <TextBlock Classes="FluentSettingsCardDescription"
                                               Text="使用深色背景减少眼疲劳" />
                                </StackPanel>
                                <u:ToggleSwitch Grid.Column="2"
                                                IsChecked="{Binding EnableDarkMode}" />
                            </Grid>
                        </Border>
                    </StackPanel>
                </Expander.Content>
            </Expander>

            <!-- 视图模式切换（FluentSegmentedItem） -->
            <StackPanel Spacing="8">
                <TextBlock Classes="WinUISubtitle" Text="视图模式" />
                <ListBox Classes="FluentSegmentedControl"
                         SelectedIndex="{Binding SelectedViewMode}">
                    <ListBoxItem Classes="FluentSegmentedItem">
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <PathIcon Data="{DynamicResource FluentIcon16RegularList}"
                                      Width="16" Height="16" />
                            <TextBlock Text="列表" />
                        </StackPanel>
                    </ListBoxItem>
                    <ListBoxItem Classes="FluentSegmentedItem">
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <PathIcon Data="{DynamicResource FluentIcon16RegularGrid}"
                                      Width="16" Height="16" />
                            <TextBlock Text="网格" />
                        </StackPanel>
                    </ListBoxItem>
                </ListBox>
            </StackPanel>

            <!-- 链接按钮（FluentHyperlinkButton） -->
            <TextBlock>
                需要帮助？请访问
                <Button Classes="FluentHyperlinkButton"
                        Command="{Binding OpenDocsCommand}">
                    在线文档
                </Button>
            </TextBlock>

        </StackPanel>
    </ScrollViewer>
</UserControl>
```

---

## 附录：插件 .csproj 模板

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputType>Library</OutputType>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
    <PluginId>YOUR-UUID-HERE</PluginId>
    <PluginName>Your Plugin Name</PluginName>
    <PluginAuthor>Your Name</PluginAuthor>
    <PluginDescription>One-line description</PluginDescription>
    <PluginVersion>1.0.0</PluginVersion>
    <MinPluginSdkVersion>1.0.0</MinPluginSdkVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="LYBox.Plugin.Generators" Version="$(PluginSdkVersion)"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <PackageReference Include="LYBox.Plugin.Shared" Version="$(PluginSdkVersion)"
                      PrivateAssets="all" />
  </ItemGroup>
</Project>
```

> `$(PluginSdkVersion)` 在 `Directory.Build.props` 中统一定义。所有插件必须设置 `AvaloniaUseCompiledBindingsByDefault=true`。
