# 侧边栏导航改版分析：Win11 设置风格侧边栏

> 目标样式参考：截图（Windows 11 设置/影音类应用侧边栏）
> 分析对象：LYBox 当前主题程序（Avalonia + Ursa 控件库）
> 日期：2026-10-18

---

## 1. 截图目标样式拆解

截图侧边栏自上而下包含：

| 序号 | 元素 | 说明 |
|---|---|---|
| 1 | ← 返回箭头 | 顶部左侧，页面回退 |
| 2 | ☰ 汉堡按钮 | 折叠/展开侧边栏 |
| 3 | 搜索框 | **位于侧边栏内部**，通栏，占位文案「请输入内容」，左侧放大镜图标 |
| 4 | 菜单行 | 图标 + 文字：主页 / GitHub / 订阅 / 历史 / 下载 / 未播放的剧集 / 正在播放 |
| 5 | 分组分隔线 | 菜单组之间的细线（历史/下载 下方可见） |
| 6 | 可展开项 | 「历史」「下载」右侧带 chevron（▼），可展开子项 |
| 7 | 底部区 | 分隔线 + 「设置」 |
| 8 | 内容区 | 右侧为大面积**圆角卡片**，与侧边栏在视觉上分离 |
| 9 | 侧边栏底色 | 浅色、半透明（Mica 质感），通高 |

---

## 2. 当前实现现状（LYBox 主题程序）

### 2.1 架构与关键文件

| 文件 | 职责 |
|---|---|
| `src/Layout/LYBox.Layout.Ursa/Views/MainWindow.axaml` | UrsaWindow 外壳：LeftContent=汉堡按钮、TitleBarContent=搜索框（居中 360px）、RightContent=主题切换 |
| `src/Layout/LYBox.Layout.Ursa/Views/MainView.axaml` | 主体布局：`Grid Margin="0,48,0,0"`，列0=导航面板，列1=内容区 |
| `src/Layout/LYBox.Layout.Ursa/ViewModels/MainViewViewModel.cs` | Menus / IsCollapsed / SearchText（300ms 防抖）/ SelectedMenuItem / Content / Activate(key) |
| `src/Layout/LYBox.Layout.Ursa/Services/MenuConfigurationService.cs` | 菜单结构：Introduction 首页 + 插件注册项 |
| `src/Plugin/LYBox.Plugin.Shared/ViewModels/MenuItemViewModel.cs` | MenuHeader / MenuIconName / Key / Children / **IsSeparator** / Order / Group |
| `src/Layout/LYBox.Layout.Ursa/Theme/FluentDesign/*` | Fluent 设计体系：FluentNavPane、FluentMainContentSurface、NavMenuItem 选中态覆盖等 |
| `src/Layout/LYBox.Layout.Core/Resources/Strings*.resx` | 本地化（NAV_* / SEARCH_PLACEHOLDER / TOOLTIP_TOGGLE_NAV 等） |

### 2.2 现有能力盘点（对照截图逐项核对）

| 截图特性 | 当前状态 | 结论 |
|---|---|---|
| ☰ 汉堡折叠 | `u:UrsaWindow.LeftContent` 中已有，命令 `ToggleCollapseCommand` → `IsCollapsed` | ✅ 已具备 |
| 侧边栏图标+文字行 | `u:NavMenu`（ItemsSource=Menus.MenuItems，IconTemplate=TwoTonePathIcon） | ✅ 已具备 |
| 选中态强调 | FluentDesignStyles 已覆盖 NavMenuItem：accent 底色 + 左侧 2px 竖条 + 文字 SemiBold | ✅ 已具备（风格略有差异，可微调） |
| 可展开子菜单（chevron） | NavMenu 原生支持 `SubMenuBinding=Children`，`PART_ExpanderIcon` 带旋转动画 | ✅ 已具备 |
| 分组分隔线 | NavMenu 主题内置 `IsSeparator=True` → 分隔线模板；MenuItemViewModel 已有 IsSeparator 字段 | ⚠️ 能力具备，但 MenuConfigurationService 未产出分隔项 |
| 底部「设置」 | 侧边栏底部 StackPanel：分隔线 + 设置 + **插件管理**（两个按钮） | ⚠️ 已具备，截图仅一个「设置」 |
| 搜索框 | 位于**窗口标题栏中央**（360px 居中），非侧边栏内 | ❌ 需迁移 |
| ← 返回按钮 | 无任何实现；无导航历史概念 | ❌ 需新增 |
| 内容区圆角卡片 | `FluentMainContentSurface`：CornerRadius `8,0,0,0`（仅左上圆角） | ⚠️ 需改为全圆角 |
| 侧边栏宽度 | `FluentPaneExpandedWidth=280` / `CollapseWidth=48`（Light/Dark 主题字典） | ⚠️ 截图约 250–260，可微调 |
| 半透明 Mica 侧边栏 | `ApplyBackdropBrushes()` 已在 Mica 可用时替换 FluentShellMicaBrush | ✅ 已具备 |
| 搜索能力 | SearchText → 300ms 防抖 → `WeakReferenceMessenger "SearchChanged"` → 首页过滤 | ✅ 已具备（迁移后绑定不变） |

---

## 3. 所需改动清单（按文件）

### 3.1 `Views/MainWindow.axaml`（小改）

- **LeftContent**：在汉堡按钮前增加「← 返回」按钮：
  - `Command="{Binding MainViewViewModel.BackCommand}"`，`IsVisible="{Binding MainViewViewModel.CanGoBack}"`（无历史时隐藏）或常显（点击跳首页）。
  - 图标需新增 Fluent 路径资源（如 `FluentArrowLeft20Regular`，需确认图标资源字典中是否已有，无则补一条 PathIcon Data）。
- **TitleBarContent**：移除搜索框（搜索框迁入侧边栏），标题栏回归纯拖拽caption 区。移除后需验证窗口拖拽区域仍充足（ UrsaWindow 空 TitleBarContent 时整条标题栏可拖拽）。

### 3.2 `Views/MainView.axaml`（中改）

- 在 `Border Classes="FluentNavPane"` 内、NavMenu 上方新增搜索框：
  ```xml
  <TextBox Classes="FluentSearchBox"
           Margin="8,4,8,8"
           Text="{Binding SearchText, Mode=TwoWay}"
           PlaceholderText="{DynamicResource SEARCH_PLACEHOLDER}"
           IsVisible="{Binding !IsCollapsed}">
      <TextBox.InnerLeftContent>
          <Path Width="14" Height="14" Data="{DynamicResource FluentSearch20Regular}" ... />
      </TextBox.InnerLeftContent>
  </TextBox>
  ```
- 注意：原 MainView 是 `Grid RowDefinitions="*,Auto"`（NavMenu + 底部区），需改为 `Auto,Auto,*` 或把搜索框放入一个顶部容器。
- 底部区维持「设置」；「插件管理」二选一：保留（双按钮）或并入菜单列表上部（如需严格贴图则移除底部按钮、在菜单数据中注册）。

### 3.3 `ViewModels/MainViewViewModel.cs`（中改，仅完整版需要）

- 新增导航历史：
  - `Stack<string> _backStack`（或 `List<string>` 支持前进）；`OnNavigation` 入栈；
  - `[ObservableProperty] private bool _canGoBack;`
  - `[RelayCommand] void Back()`：出栈并导航到上一 Key，更新 CanGoBack；
  - `ToggleSidebar` 已存在但未被 UI 使用，可清理或绑定到返回按钮语义。
- 现有搜索防抖 / "JumpTo" 消息机制不变。

### 3.4 `Theme/FluentDesign/FluentDesignStyles.axaml` + `Light.axaml` / `Dark.axaml`（小改）

- `FluentPaneExpandedWidth`：280 → 约 256（贴图宽度）。
- `FluentMainContentSurface`：`CornerRadius` `8,0,0,0` → `8`（全圆角）；按截图效果可给内容区加少量外边距（如 `Margin="0,0,8,8"` 由 MainView 布局控制）。
- NavMenuItem 行高/圆角微调（如 hover 圆角 4、行高 36）——按截图 plain row 观感，可选。

### 3.5 `Services/MenuConfigurationService.cs`（小改，可选）

- 若需分组分隔线：在菜单数据中插入 `IsSeparator = true` 的项（插件注册侧同步约定）。Ursa NavMenu 已内置分隔线渲染，无需改控件。

### 3.6 本地化 `Strings.resx` / `Strings.zh-CN.resx`（小改）

- 新增：`TOOLTIP_BACK`（返回）、如移除标题栏搜索需确认 `SEARCH_PLACEHOLDER` 文案是否改为「请输入内容」（当前值需核对，截图为中文「请输入内容」）。
- `TOOLTIP_TOGGLE_NAV` 已存在，复用。

---

## 4. 工作量估算

### 方案 A：布局对齐版（不含导航历史）—— 约 0.5～1 人天

| 任务 | 预估 |
|---|---|
| 搜索框从标题栏迁入侧边栏（MainWindow/MainView XAML + 折叠态隐藏） | 0.5 天 |
| 返回按钮（常显，点击回首页；或先隐藏占位） | 0.5 天 |
| 内容区全圆角 + 侧边栏宽度微调 | 0.5 天 |
| 冒烟测试（亮/暗主题、折叠动画、搜索回归） | 0.5 天 |

### 方案 B：完整版（推荐）—— 约 2～3 人天

| 任务 | 预估 |
|---|---|
| 方案 A 全部 | 1～1.5 天 |
| 导航历史栈（BackCommand/CanGoBack/前进后退 + 边界处理） | 0.5～1 天 |
| 分组分隔线（菜单数据 + 插件注册约定） | 0.5 天 |
| NavMenuItem 行样式精修（贴图观感） | 0.5 天 |
| 本地化补齐 + 全量回归（i18n 切换、Mica、Linux/macOS 回退路径） | 0.5 天 |

> 单人全职计；含代码审查与文档更新建议按 +20% 预留。

---

## 5. 风险与注意事项

1. **标题栏拖拽区**：搜索框移出标题栏后，TitleBarContent 为空，需确认 UrsaWindow 下整条标题栏仍可拖拽、双击最大化不受影响（重点回归）。
2. **折叠态交互**：`IsCollapsed=True`（48px）时搜索框隐藏；展开/折叠动画期间搜索框显隐与宽度过渡是否顺畅需目检。
3. **返回按钮语义**：无历史时建议隐藏（`CanGoBack`）而非置灰，避免「← 无反应」的困惑；若产品上要求常显，则约定为「回首页」。
4. **分隔线对插件生态的影响**：`IsSeparator` 项已存在于 MenuItemViewModel，但插件侧若从未产出过，需在 `Plugin-API-Reference.md` 补充约定。
5. **底部按钮去留**：截图的底部只有「设置」。当前有「设置 + 插件管理」两个。直接移除插件管理入口会损失功能可达性，建议将插件管理移入菜单列表（如「设置」上方或分组内），与产品确认后再动。
6. **内容区圆角与 Mica 协调**：全圆角卡片 + 半透明侧边栏在 Mica 下的层次感需目检；必要时给内容卡片加 1px 边框（`FluentShellContentBorderBrush` 已有）。
7. **菜单数据源**：当前实际菜单只有 Introduction 一项 + 插件动态注册，截图中的多分组（订阅/历史/下载…）属于示例数据；若 LYBox 自身要长出这些分组，需在 MenuConfigurationService 中预注册，属于产品决策而非纯样式工作。

---

## 6. 结论

- 截图样式与当前实现的**架构同源**（WinUI NavigationView 范式），主体（NavMenu、汉堡折叠、选中态、Mica）已就绪，**不需要重写导航框架**。
- 核心增量只有三项：**① 搜索框迁入侧边栏；② 新增返回按钮 + 导航历史；③ 内容区圆角卡片化 + 侧边栏宽度/分组微调**。
- 建议走**方案 B（2～3 人天）**一次到位；若只想先看视觉效果，可先落地方案 A（0.5～1 人天），返回按钮暂时隐藏或绑定回首页。

---

## 7. 方案 B 实施记录（2026-10-18 已落地）

### 7.1 改动文件

| 文件 | 改动 |
|---|---|
| `src/Layout/LYBox.Layout.Ursa/Views/MainWindow.axaml` | LeftContent 改为「返回 + 汉堡」按钮组；移除 TitleBarContent 搜索框，标题栏中部留作拖拽区 |
| `src/Layout/LYBox.Layout.Ursa/Views/MainView.axaml` | 侧边栏改为 `Auto,*,Auto` 三行：顶部搜索框（折叠态隐藏）+ NavMenu + 底部设置/插件入口 |
| `src/Layout/LYBox.Layout.Ursa/Views/MainView.axaml.cs` | 订阅 `NavMenu.ContainerPrepared`，把数据项 `IsSeparator` 同步到 `NavMenuItem` 容器（双向，兼容容器回收） |
| `src/Layout/LYBox.Layout.Ursa/ViewModels/MainViewViewModel.cs` | 新增 `BackCommand` / `CanGoBack` / 回退栈；导航统一走 `NavigateTo`；按 `Group` 变化自动插入分隔项 |
| `src/Layout/LYBox.Layout.Ursa/Theme/FluentDesign/FluentDesignStyles.axaml` | `FluentMainContentSurface` 改全圆角 + 四边 8px 外边距；新增 `FluentHamburger:disabled` 置灰样式 |
| `src/Layout/LYBox.Layout.Ursa/Theme/FluentDesign/Light.axaml` / `Dark.axaml` | `FluentPaneExpandedWidth` 280 → 256 |
| `src/Plugin/LYBox.Plugin.Shared/Attributes/MenuAttribute.cs` | 新增 `Group` 属性 |
| `src/Plugin/LYBox.Plugin.Generators/MetadataGenerator.cs` | 解析并输出 `Group` 到菜单描述符 |
| `src/Plugin/LYBox.Plugin.Shared/Generated/GeneratedPluginModule.cs` | `GeneratedMenuDescriptor` 新增 7 参数重载 + `Group` 属性（保留 6 参数重载，旧插件 DLL 二进制兼容） |
| `src/Plugin/LYBox.Plugin.Shared/Generated/GeneratedUiDescriptorExtensions.cs` | `ToMenuItems` 映射 `Group` |
| `src/Layout/LYBox.Layout.Core/Resources/Strings.resx` / `Strings.zh-CN.resx` | 新增 `TOOLTIP_BACK`（Back / 返回）；中文 `SEARCH_PLACEHOLDER` 改为「请输入内容」 |
| `docs/Plugin-API-Reference.md` | 补充 `[Menu(..., Group = ...)]` 与分隔线约定 |

### 7.2 关键行为

- **返回**：`CanGoBack` 为 false 时按钮置灰但保留占位（避免汉堡左右跳动）；每次真实页面切换才入栈，`Back` 出栈不会反向记录。
- **搜索**：绑定与 300ms 防抖逻辑完全复用，仅位置从标题栏迁到侧边栏；折叠态自动隐藏。
- **分隔线**：相邻菜单项 `Group` 不同时自动插入；未设置 `Group` 的菜单（当前默认菜单）行为与旧版一致。

### 7.3 验证

- `dotnet build Core.slnx`：**0 错误**（14 条既有/分析器警告）。
- 运行时冒烟：`LYBox.Launcher.Desktop.exe --no-splash` 正常启动、进程响应，日志无异常。
- 待人工目检：亮/暗主题、Mica 下卡片层次、折叠动画、窗口拖拽区、返回按钮点击链路。

---

## 8. 工具坞布局改版（2026-10-19 已落地）

> 目标：只借鉴参考截图的**布局结构**（左侧通高侧边栏 + 顶部通栏 Hero + 三列工具卡片网格），
> 配色与控件观感继续沿用项目既有 Fluent/WinUI 令牌，不引入第三方色板。

### 8.1 结构变化

| 区域 | 改版前 | 改版后 |
|---|---|---|
| 标题栏 | 左：返回 + 汉堡；右：主题切换 | 左：应用图标 + 品牌名；右：主题切换 |
| 侧边栏 | 搜索 + NavMenu + 底部设置/插件管理 | 顶部：折叠（有历史时并出返回）→ 搜索 → NavMenu → 底部「管理扩展 / 设置」 |
| 内容区 | 四边 8px 留白 + 8px 圆角浮动卡片，内容再让 36,24 | 与侧边栏贴边的扁平区域，页面自行负责内边距 |
| 首页 | 分组标题 + 可换行的紧凑按钮条 | 通栏 Hero（欢迎语 + 版本号）+ 三列卡片网格（图标方块 / 标题 / 描述 / Pin） |
| 宿主菜单 | Introduction + FluentControls | 仅 Introduction（主页）；目录与工具全部由插件 `[Menu]` 注册 |

### 8.2 改动文件

| 文件 | 改动 |
|---|---|
| `Views/MainWindow.axaml` | LeftContent 改为图标 + 品牌名，折叠/返回按钮迁入侧边栏 |
| `Views/MainView.axaml` | 侧边栏四行结构（按钮区 / 搜索 / NavMenu / 底部入口）；内容区 `ContentControl` 去掉 36,24 外边距 |
| `Pages/IntroductionDemo.axaml` | Hero + `UniformGrid Columns=3` 卡片网格；卡片含图标方块、标题、描述、Pin |
| `ViewModels/IntroductionDemoViewModel.cs` | 工具列表拍平为 `FilteredItems`（分组名作描述兜底）；新增 `WelcomeTitle` / `VersionText`；一级叶子菜单也作为卡片 |
| `Theme/FluentDesign/FluentDesignStyles.axaml` | `FluentMainContentSurface` 去圆角/去外边距；新增 `FluentRailButton` / `FluentHeroBanner` / `FluentHeroTitle` / `FluentHeroVersion` / `FluentToolCard` / `FluentToolIconTile` 等布局样式 |
| `Theme/FluentDesign/Light.axaml` / `Dark.axaml` | `FluentPaneExpandedWidth` 256 → 250，折叠宽 48 → 52 |
| `Services/MenuConfigurationService.cs` | 移除内置 FluentControls 菜单项 |
| `MenuItemTreeBuilder.cs` / `FallbackIcons.cs` | 未声明 `IconName` 的菜单项按 Key 稳定散列取通用 Fluent 图标 |
| `MenuItemViewModel.cs` | 新增 `Description`（卡片次级文案，为空回退分组名） |
| `Strings*.resx` | `NAV_Introduction` → 所有工具 / All tools；`NAV_Plugins` → 管理扩展；搜索占位文案；新增 `HOME_WELCOME_TITLE` |

### 8.3 验证

- `dotnet build Core.slnx`：0 错误。
- 运行时（外部插件路径 `AVALONIA_EXTRA_PLUGINS_PATH`）目检：侧边栏仅「所有工具」+ 插件目录；首页 Hero 版本号取自 `AssemblyInformationalVersion`；三列卡片网格与搜索过滤正常。

---

## 9. 独立测试插件与首页卡片校验（2026-10-20 已落地）

> 目标：用**主体程序之外**的独立插件产出真实菜单数据，目检首页卡片网格 / 侧边栏分组 / 状态徽标；
> 演示案例一律不进主体程序（`MenuConfigurationService` 保持仅「主页」一项）。

### 9.1 测试插件

`tests/LYBox.DemoPlugin/`（不加入 `Core.slnx`，不随宿主发布）：

| 文件 | 内容 |
|---|---|
| `LYBox.DemoPlugin.csproj` | `IsPluginProject=true` + `GeneratePluginManifest=true`；引用 `LYBox.Plugin.Generators`（analyzer）与 `LYBox.Plugin.Shared`（项目引用） |
| `DemoPlugin.cs` | `[GenerateMetadata] partial class DemoPlugin : IPluginMetadata`（元数据由 csproj 注入） |
| `ViewModels/DemoToolViewModels.cs` | 7 个 VM：父组「编码器」+ Base64 / URL / JSON 三个子项 + 时间戳 / 颜色 / 正则三个顶级项；分别带 `Beta` / `New` 状态与描述 |
| `Views/DemoToolView.axaml` | 单一共享视图（标题 + 描述占位） |

加载方式（本地开发）：

```powershell
dotnet publish tests/LYBox.DemoPlugin/LYBox.DemoPlugin.csproj -c Debug -o artifacts/publish/LYBox.DemoPlugin
# 暂存目录必须「干净」：宿主会把插件目录下每个 *.dll 当作独立插件
New-Item -ItemType Directory -Force artifacts/dev-plugins/LYBox.DemoPlugin
Copy-Item artifacts/publish/LYBox.DemoPlugin/LYBox.DemoPlugin.dll, `
          artifacts/publish/LYBox.DemoPlugin/LYBox.DemoPlugin.deps.json, `
          artifacts/publish/LYBox.DemoPlugin/plugin.json, `
          artifacts/publish/LYBox.DemoPlugin/shared-assemblies.txt artifacts/dev-plugins/LYBox.DemoPlugin/
$env:AVALONIA_EXTRA_PLUGINS_PATH = "<repo>\artifacts\dev-plugins"
dotnet run --project src/App/LYBox.Launcher.Desktop
```

### 9.2 本轮由测试插件暴露并修复的宿主缺陷

| 缺陷 | 原因 | 修复 |
|---|---|---|
| 首页卡片挤在网格列左侧，右侧留大片空白 | `UniformGrid` 只给单元格尺寸，卡片自身未拉伸即按内容宽度收缩 | `FluentToolCard` 补 `HorizontalAlignment=Stretch` + `HorizontalContentAlignment=Stretch` |
| 插件无法提供卡片次级文案 | `[Menu]` 缺 `Description`，attribute → 生成器 → 描述符链路断开 | `MenuAttribute.Description` → `MetadataGenerator` → `GeneratedMenuDescriptor.Description` → `MenuItemViewModel.Description` |
| 插件状态徽标渲染为普通内联文字 | Ursa `u:Badge` 的胶囊容器仅在 `Header` / `Dot` 非空时可见，原实现绑的是 `Content` | 改绑 `Header` 并加 `Light Primary` 类；`ToolStatus` 样式覆写圆角 4、字号 11、胶囊内边距 |
| `dotnet publish` 的插件产物缺 `plugin.json` | 构建期直接写盘的文件不属于 MSBuild 复制项集合 | `GeneratePluginManifest` 增补 `Publish` 到 `AfterTargets`，并显式写 `$(PublishDir)plugin.json` |

兼容性：`GeneratedMenuDescriptor` 保留 6 参 / 7 参重载，旧插件程序集不受影响。

### 9.3 验证

- `dotnet build` 宿主与测试插件：0 错误。
- 运行时目检：六张卡片三列撑满，描述与状态胶囊徽标可见，侧边栏「编码器」分组可展开并正确高亮，点击叶子项可导航到插件页面。
- 无插件时首页显示「尚未安装任何工具」；有搜索词且无匹配时显示「无搜索结果」。
- 深色 / 浅色主题下卡片与徽标均正常。

---

## 10. DevToys 源码级布局对比（2026-10-21）

> 基准：DevToys v2 真实 UI 源码 `src/app/dev/DevToys.Blazor`（Blazor + WinUI3 混合），
> 而非截图推测。本节只做**对比与结论**，不含代码改动。

### 10.1 DevToys 布局骨架

| 层级 | 文件 | 事实 |
|---|---|---|
| 外壳 | `Components/Menu/NavBar/NavBar.razor` + `.razor.scss` | `.nav-bar-root` 为 CSS Grid，`grid-template-areas: "header header" / "sidebar content"`；顶栏行高 48px（`--sidebar-top-bar-height-hard`） |
| 侧栏 | 同上 | `nav` 为三段栅格 `grid-template-rows: auto 1fr auto` → `sidebar-header` / `sidebar-body`（ScrollViewer + `ul.sidebar-items`）/ `sidebar-footer`（`li.sidebar-item-separator` + `FooterMenuItemsSource` + `custom-footer` 插槽） |
| 宽度 token | `NavBar.razor.scss` | `$sidebar-expanded-width: 320px` / `$sidebar-collapsed-width: 49px` / `$sidebar-hidden-width: 0px` |
| 状态机 | `NavBarState.cs` | 三态 `Hidden` / `Collapsed` / `Expanded` + `ExpandedOverlay`（折叠态汉堡唤出浮层覆盖内容区）；`UserPreferredState` 记录用户意图，`WidthUpdated()` 按宽度自动切换 |
| 阈值 | `NavBarThresholds.cs` | `NavBarWidthSidebarHiddenThreshold = 640`、`NavBarWidthSidebarCollapseThreshold = 1008` |
| 侧栏项 | `NavBarItem.razor` + `.scss` | ListBoxItem 形态，`grid-template-columns: auto 1fr auto`，gap `0 16px`；图标 16×16；子项 `padding-left: 42px`；展开按钮 32 宽 × 34 高；分隔线 `height:1px; margin:4px 0` |
| 内容区 | `NavBar.razor.scss` | `main` 仅左上圆角 `border-top-left-radius: var(--overlay-corner-radius)` + `navigation-view-content-grid-border` |
| 首页 | `SubPages/ToolGroup.razor` + `.scss` | Hero 250px 高、mask 渐隐、`rotateX(15deg) rotateZ(-15deg) skewX(15deg)` 3D 视差；`GridView ItemMinWidth=330` 自适应列宽；工具卡片 `height:134px`，栅格 `grid-template-areas: "icon title buttons" / "icon description description"`；图标瓦片 100×100（padding 12，内含 64px 字体图标）；描述 5 行 clamp；右上 24×24「在新窗口打开」按钮；页脚 `InfoBar` |
| 工具页 | `SubPages/ToolPage.razor` + `.scss` | 标题 24px + 右上工具条（收藏按钮），页面内边距 `--tool-page-padding` |
| 搜索 | `Index.razor` / `NavBar.razor.ts` | `AutoSuggestBox` 带下拉结果（图标 + 匹配文本高亮），提交/变更双回调；`Ctrl/Cmd+F` → `ForceExpand()` + 200ms 后聚焦 |

### 10.2 逐区差异清单

| 区域 | 维度 | DevToys | LYBox 现状 | 差异级别 |
|---|---|---|---|---|
| 侧栏 | 展开宽度 | 320px | `FluentPaneExpandedWidth` = 260 | 中 |
| 侧栏 | 折叠宽度 | 49px | `FluentPaneCollapsedWidth` = 48 | 低 |
| 侧栏 | 状态机 | Hidden / Collapsed / Expanded(+Overlay) 四态 | 仅 Expanded / Collapsed 两态；`IsSidebarHidden` + `ToggleSidebar()` 为**死代码**（无任何 UI 绑定） | 高 |
| 侧栏 | 窄窗行为 | <640px 侧栏完全隐藏，顶栏补汉堡 | 无 Hidden 态，窄窗仅自动折叠 | 高 |
| 侧栏 | 顶段结构 | `sidebar-header` 内汉堡 + 折叠态搜索按钮 + AutoSuggestBox 同段 | 汉堡独占 Row0，搜索独占 Row1 | 低 |
| 侧栏 | 搜索控件 | `AutoSuggestBox` + 下拉结果列表 | 普通 `TextBox` + 清除/搜索双按钮，仅过滤首页网格 | 高 |
| 侧栏 | 导航项图标 | 16×16 | `TwoTonePathIcon` 20×20 | 低 |
| 侧栏 | 子项缩进 | `padding-left: 42px` | `SubMenuIndent = 24` | 中 |
| 侧栏 | 展开按钮 | 32 宽 × 34 高 | 沿用 Ursa 模板默认 | 低 |
| 侧栏 | 底部区 | 复用 `NavBarItem` 列表 + 标准分隔线 + 可插拔 footer 插槽 | 两个独立 `FluentNavFooterItem` 按钮 + 自绘 `Rectangle` 分隔线 | 中 |
| 顶栏 | 返回按钮 | `IsEnabled="@CanGoBack"`，无历史时禁用 | 常显可点，无历史时回首页 | 中 |
| 顶栏 | 折叠唤出 | Hidden/Collapsed 态顶栏出现汉堡唤出 Overlay | 顶栏无汉堡 | 中 |
| 内容区 | 边框 | 四边 `navigation-view-content-grid-border` + 左上圆角 | `FluentMainContentSurface` `BorderThickness="1,1,0,0"` + `CornerRadius="8,0,0,0"` | 低 |
| 首页 | Hero | 250px 背景图 + mask 渐隐 + 3D 视差动画 | `FluentHeroBanner` MinHeight 90 纯渐变条，无动画 | 高 |
| 首页 | 网格 | `GridView ItemMinWidth=330` 自适应列 | `UniformGrid Columns="3"` 固定三列 | 中 |
| 首页 | 卡片高度 | 134px | `FluentToolCard` Height 96 | 中 |
| 首页 | 卡片栅格 | `icon / title+buttons / description` 三区 | 标题 + 徽标同行、描述另起行，右上仅图标 | 低 |
| 首页 | 图标瓦片 | 100×100（内含 64px 字体图标） | `FluentToolIconTile` 56×56（内含 26×26 PathIcon） | 中 |
| 首页 | 描述 | 5 行 clamp | `MaxLines=2` | 中 |
| 首页 | 卡片动作 | 右上 24×24「在新窗口打开」按钮 | 右上 20×20 `FluentOpen20Regular` 静态图标 | 低 |
| 首页 | 页脚 | `InfoBar` 提示条 | 无 | 低 |
| 工具页 | 规范 | 标题 24px + 右上工具条 + `--tool-page-padding` | 插件页无统一标题/内边距规范 | 高 |

### 10.3 结论与建议落地顺序

本轮对比确认：LYBox 的**骨架已与 DevToys 同源**（48px 顶栏 + 侧栏 + 内容区的栅格分层、1008px 折叠阈值、Ctrl+F 聚焦、选中指示条均已对齐），
剩余差异集中在**尺寸规格**、**第三态（Hidden）**、**搜索交互形态**与**首页视觉规格**四处。

建议按以下顺序落地（每步独立可回滚）：

1. **侧栏尺寸与状态机**：展开宽 260 → 320、折叠 48 → 49；补 Hidden 态（<640px 隐藏侧栏并在顶栏补汉堡），清理 `IsSidebarHidden` / `ToggleSidebar()` 死代码，改为真正驱动 `NavBarState` 三态。
2. **顶栏返回按钮语义**：改为绑定 `CanGoBack`，无历史时禁用；Hidden 态顶栏补汉堡按钮。
3. **搜索 AutoSuggest 化**：侧栏搜索框换成带下拉结果的组合控件，命中项点击直接导航；保留 Ctrl+F 行为。
4. **首页视觉规格**：Hero 提到 250px 并加渐隐遮罩；卡片 96 → 134、瓦片 56 → 100、描述 2 → 5 行；网格改自适应列宽（最小 330）。
5. **工具页规范**：统一 24px 标题 + 右上工具条 + 页面内边距，落到插件页模板上。

第 1、2 步属结构级、影响面小；第 3、4 步是观感差异的主要来源；第 5 步涉及插件模板，需先定契约。

---

## 11. 落地实施记录（2026-10-22）

按 10.3 的 5 步顺序实施，每步独立可回滚。涉及文件：

| 步骤 | 主题 | 主要改动 |
|---|---|---|
| 1 | 侧栏尺寸与状态机 | 展开宽 260→320、折叠 48→49（`Light.axaml` / `Dark.axaml` 的 `FluentPaneExpandedWidth` / `FluentPaneCollapsedWidth`）；新增 `IsSidebarHidden` + 双阈值策略（<640 隐藏、<1008 折叠，`UserPreferredCollapsed` 记录用户意图）；删除死代码 `ToggleSidebar()`，改为 `ShowSidebarCommand`；`SubMenuIndent` 24→42；`MainWindow.MinWidth` 640→480（否则 Hidden 态不可达） |
| 2 | 顶栏语义 | 返回按钮绑定 `CanGoBack` 置灰禁用；Hidden 态顶栏补汉堡按钮唤出侧栏 |
| 3 | 搜索 AutoSuggest 化 | 新增 `SearchSuggestion` + `FilteredSuggestions` 下拉（TextBox + Popup + ItemsControl，回车选中第一条、点击直达）；保留 Ctrl+F 展开聚焦 |
| 4 | 首页视觉规格 | Hero 90→250px 并叠渐隐遮罩 `FluentHeroMaskBrush`；卡片 96→134、图标瓦片 56→100（图标 26→64）、描述 2→5 行；网格改自适应列（最小 330，页面按宽度回写 `GridColumns`） |
| 5 | 工具页规范 | 宿主新增 `FluentToolPage*` 样式契约（页面容器 / 24px 标题 / 副标题 / 右上工具条 / 32×32 动作按钮），落在独立测试插件 `tests/LYBox.DemoPlugin` 作参考实现 |

### 11.1 关键取舍

- **AutoSuggest 实现**：Avalonia `AutoCompleteBox` 依赖主题模板、API 不确定；`TextBlock.Inlines` 是 DirectProperty 不可绑定（无法做匹配高亮）。最终采用 `TextBox + Popup + ItemsControl`，规避主题依赖，代价是暂不做 per-run 高亮。
- **自适应网格**：`UniformGrid` 无 `ItemMinWidth` 能力（仅 Rows/Columns/Spacing），改为页面按 `floor((width + 16) / (330 + 16))` 计算列数并回写 VM。
- **工具页契约落点**：本机无 `..\LYBox.Plugins` 仓库，插件模板无法直接改；改为「宿主提供 Application 级样式契约 + 独立测试插件作参考实现」，插件页直接引用类名即可。
- **Hidden 态可达性**：原 `MinWidth=640` 使 `width < 640` 永不成立，Hidden 态仍是死代码；降到 480 后第三态真实可达。

### 11.2 验证

- `dotnet build src/App/LYBox.Launcher.Desktop` 与 `dotnet build tests/LYBox.DemoPlugin`：0 错误。
- 启动冒烟：无异常日志，插件页样式与下拉未触发运行时错误。
- 待补：界面目检（本机无桌面自动化工具，`orca` CLI 不可用）。
