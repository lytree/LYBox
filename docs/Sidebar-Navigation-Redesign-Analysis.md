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
