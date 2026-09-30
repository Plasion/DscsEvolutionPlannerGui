# DscsEvolutionPlanner（WPF 桌面版）

WPF 界面，目标框架 `net10.0-windows`（XAML + 数据绑定，`UseWPF=true`）。

**算法就在本工程里**（`Core/` 下的 C# 实现），运行时不依赖 Python、不启动任何子进程；
只读两份数据文件：`data.csv`（数码兽数据）和 `Data/PinyinTable.json`（拼音表）。

---

## 环境与构建

- 只需要 **.NET 10 SDK**（运行需要 .NET 10 桌面运行时）
- 不需要装 Python，也不需要 mohu

```powershell
cd dotnet
dotnet run                 # 或者 dotnet build 后直接跑 bin\Debug\net10.0-windows\DscsEvolutionPlanner.exe
```

Visual Studio 里直接打开 `DscsEvolutionPlanner.slnx`。

### 数据文件从哪来

| 文件 | 来源 | 说明 |
| --- | --- | --- |
| `data.csv` | `../py/data.csv`，构建时复制到输出目录 | 单一数据源，改 py 侧的数据即可；程序按 `--data` → 设置里记的 → exe 同级 → 向上找 `data.csv` / `py/data.csv` 的顺序查找 |
| `Data/PinyinTable.json` | 由 `tools/gen_pinyin_table.py` **开发期**生成一次 | 只影响拼音模糊匹配；缺了也能跑（退化为纯字符匹配） |
| `config.json` | 本工程的 `dotnet/config.json`，构建时复制到输出目录 | 阈值 / 上限 / 默认尺寸，见下面「可调参数」；程序从 exe 目录往上找，允许写 `//` 注释 |

拼音表在数据变化后需要重新生成（只有这一个脚本用到 Python，且它不是运行时依赖）：

```powershell
python tools\gen_pinyin_table.py     # 需要 pypinyin + mohu（用于补全音节表）
```

程序启动时会核对表的 `keyHash` 与 `data.csv` 是否同源，不一致会在状态栏提示。

### 目录结构

工程按 **view / viewmodel / Core** 三层划分，全工程只有 **4 个 XAML**（`App.xaml` 装主题，其余三个是界面）：

| 目录 / 文件 | 放什么 |
| --- | --- |
| `App.xaml(.cs)` | 应用入口 + **全部主题资源**（画刷、字体、控件样式、转换器实例）；无界面模式（`--cli`）也在这里 |
| `view/` | 三个视图与它们的最小 code-behind：`ShellView`（底层壳）、`EvolutionRouteView`（进化线计算器）、`IllustratedGuideView`（图鉴）；另有纯 C# 的自定义控件与附加行为（见下） |
| `view/Controls/` | 自绘 / 自定义控件：`CodexGraphHost`（关系网宿主：缩放 + 拖动）、`CodexGraph`（关系网排布 + 画线）、`RouteChain`（蛇形链）、`VirtualizingMasonryPanel`（虚拟化瀑布流）、`SuggestBox`（名字候选输入框）、`SkillPicker`（多选继承技下拉） |
| `view/AttachedCommand.cs` · `view/Converters.cs` · `view/WpfDialogService.cs` | 把点击/双击接到命令上的附加属性、资源键 → 画刷的转换器、文件对话框实现 |
| `viewmodel/` | 绑定用的 VM：`MainViewModel`（三个分部文件：构造与命令 / 启动与表单 / 结果与图鉴）、`RouteVm` / `CodexVm` / `DigimonDetailVm` / `Lists` / `NotifyBase`，以及 `Command`（ICommand）与 `Dialogs`（`IDialogService` / `IWindowActions` 抽象） |
| `Core/` | 数据、算法与核心服务（见下表） |
| `config.json` | 可调参数（见「可调参数」一节） |
| `Data/`、`imgs/`、`assets/`、`tools/` | 拼音表、图片、开发期脚本 |

分层约定（重构后强制遵守）：

- **view 只通过 DataContext + 绑定与 viewmodel 交互**：XAML 里没有任何事件处理器（`Click=` / `MouseLeftButtonUp=` / `TextChanged=` …），
  也不从页面取控件（`x:Name` 只出现在控件模板内部，用于 `PART_*` 模板部件）。三个视图的 code-behind 只有
  `InitializeComponent()`；`ShellView.xaml.cs` 额外承担两件事：把窗口动作（最小化/最大化/关闭）接到视图模型、离屏截图。
- **viewmodel 只通过属性/方法与 Core 交互**：`MainViewModel` 依赖 `PlannerFacade`（算法与呈现）、`DataSource`（数据来源）、
  `AppSettings`（config.json 快照）、`UserState`（app.settings.json）与两个抽象 `IDialogService` / `IWindowActions`。
- 点击类交互统一走 `view:AttachedCommand.ClickCommand` / `DoubleClickCommand` 绑定到 VM 上的 `Command`；
  颜色统一由 VM 给出**主题资源键**，视图用 `BrushKey` 转换器解析成画刷。

---

## 算法结构（Core/）

| 文件 | 职责 |
| --- | --- |
| `EvolutionPlanner.cs` | 对外主入口：`ResolveName` / `ResolveGeneration` / `ResolveSkill` 做解析，`Search(PlannerQuery)` 求路线；负责套用全部约束并把结果拼成与命令行一致的文本 |
| `PathFinder.cs` | 图搜索：BFS 找单条最短路 + Yen 算法求前 k 条（连候选路径的排序方式都与原实现一致，保证路线顺序相同） |
| `FuzzyMatcher.cs` | 名字/世代/继承技的模糊匹配：编辑距离 ≤2 召回，得分 = `0.6×字符相似度 + 0.4×拼音相似度` |
| `DigimonCatalog.cs` | 解析 `data.csv`（含 Python 字面量列、双向图补全、世代/继承技索引），并提供三套匹配器 |
| `PinyinTable.cs` | 读写拼音表：整词拼音、逐字拼音、纯拼音输入的音节切分 |
| `InputMatcher.cs` | 文本搜索的唯一入口：字面 + 拼音包含 + 错字容错 → 一份打分排名（起点/终点候选、图鉴筛选共用） |
| `AppConfig.cs` | 读 config.json（阈值 / 上限 / 默认尺寸），缺文件或缺字段就用内置默认值 |
| `AppSettingsSnapshot.cs` | `AppSettings`：上面那份配置的只读快照（VM 读它，不直接碰 JSON） |
| `UserState.cs` | `UserState`：app.settings.json 的读写（上次的数据文件、图片目录、窗口与栏宽） |
| `StartupOptions.cs` · `AppBootstrapper.cs` | 一次启动的输入，以及「命令行开关 + 上次状态 → 启动配置」的合成 |
| `DataSource.cs` | 数据文件与图片目录的来源（按 `--data` → 上次 → exe 同级 → 向上找的顺序定位） |
| `PlannerFacade.cs` | **核心 → 视图模型的唯一出口**：解析（`ResolveName` / `Describe` / `Suggest`）、求路线（`Search` → 卡片 VM）、图鉴（`Codex.BuildEntries` / `Codex.Build`）、详情（`Detail`） |
| `Presenter.cs` | `RoutePresenter`（求解结果 → 路线卡片 VM）与 `CodexPresenter`（筛选列表 + 关系网建图） |
| `ResourceKeys.cs` · `Theme.cs` | 主题资源键（VM 只产出键）与「按键取画刷」（只给自绘控件用） |
| `CliQueryParser.cs` | 把命令行参数解析成 `PlannerQuery`（供 `--cli` 无界面模式使用） |

数据流：

```
View（绑定/命令） ──► MainViewModel ──► PlannerFacade ──► EvolutionPlanner.Search
                                            │                  ├─ DigimonCatalog（名字/世代/技能 → id）
                                            │                  ├─ PathFinder（BFS + Yen，带约束位掩码）
                                            │                  └─ RouteResult / TextLines
                                            ├─ RoutePresenter ──► 路线卡片 VM
                                            └─ CodexPresenter ──► 图鉴卡片 / 关系网 VM
```

---

## 界面结构（壳 + 两页）

`ShellView` 是**壳**：自绘标题栏（含「路线 / 图鉴」切换）+ 两页视图 + 右侧详情栏 + 状态栏。
`EvolutionRouteView` 与 `IllustratedGuideView` 都用可见性绑定切换（`IsRouteMode` / `IsGuideMode`），
两页各自保留自己的视图状态（滚动位置、缩放、拖动位置），来回切不会重置。

| 文件 | 职责 |
| --- | --- |
| `view/ShellView.xaml(.cs)` | 壳：标题栏、页面宿主、详情栏、状态栏；code-behind 只做窗口动作接线与截图 |
| `view/EvolutionRouteView.xaml(.cs)` | 进化线计算器：左＝查询参数，中＝路线链（`RouteCard` 模板在本页 Resources） |
| `view/IllustratedGuideView.xaml(.cs)` | 图鉴：左＝虚拟化瀑布流（`GuideCard` 模板），中＝关系网 + 工具条 + 生成遮罩 |

交互是怎么接上的：

- 按钮、单选 / 复选、输入框、分隔条、下拉一律绑定到视图模型（`Command` 或属性）。
- 卡片 / 节点的「点击」「双击」用 `view:AttachedCommand`，参数是那只数码兽的编号：
  `ClickParameter="{Binding Id}"`，命令来自 `RelativeSource AncestorType=UserControl` 的 DataContext。
- 候选输入框与多选下拉是自定义控件（`SuggestBox` / `SkillPicker`）：键盘上下/回车/Esc、弹层开关都在控件内部处理，
  页面只给数据（`SuggestionProvider` / `ItemsSource`）与命令。
- 关系网宿主 `CodexGraphHost` 自己管滚动（滚动条隐藏，拖动 / 滚轮 / Shift + 滚轮）、Ctrl + 滚轮缩放与拖动平移；
  换中心时它订阅 VM 的 `RecenterRequested` 把新中心滚到视口正中（缩放不是 100% 时坐标要乘上缩放，见 `CenterOnRoot`）。
- 自绘标题栏（`WindowStyle=None`）最大化时，`ShellView` 自己回答 `WM_GETMINMAXINFO`，把窗口限制在显示器工作区内，
  否则 Windows 会按整块屏幕给尺寸、任务栏正好盖住最下面的状态栏。
- 左栏宽度两页共用一个值（`app.settings.json` 的 `LeftWidth`，来自 `AppSettings` 快照，两页绑同一个属性）。
- 关窗时 `ShellView` 调 `MainViewModel.SaveLayout(...)` 把窗口尺寸与栏宽写回 `app.settings.json`。

---

## 界面用法

三栏：左侧参数、中间路线图、右侧详情（点卡片切换）。

| 界面项 | 语义 |
| --- | --- || 起点数码兽 | 名字 / 别名 / 编号，支持拼音（`yagushou`）与错字（`压古兽`）模糊匹配；下方实时显示解析结果与相似度 |
| 终点模式 | 「指定终点」/「多终点」（后面填的任一只都可以作为终点）/「任意终点」（需配合途经或继承技约束） |
| 途经数码兽 | 每行一组：组内任一只经过即可，多行之间都要满足 |
| 需沿途收集的继承技 | 多选下拉（带关键字筛选），选中的技能都要求能收集到 |
| 排除世代 | 勾选后这些世代不参与计算 |
| 路径条数 / 翻转显示 | 求前 k 条路径；翻转显示顺序（用于「多起点 → 一终点」的查看方式） |

数据文件与图片目录都**不在界面上**：`data.csv` 按 `--data` → 上次记住的 → exe 同级 → 向上找的默认顺序定位，
图片目录由 `ImageStore.DefaultDirectory()` 自动挑一个「已经放了图」的候选目录（要临时换用 `--data` / `--assets`）。

其它交互：

- 窗口是自绘标题栏（`WindowStyle=None` + `WindowChrome`）：拖标题栏移动、双击最大化/还原，右上角是自绘的最小化 / 最大化 / 关闭；系统贴边、缩放边框都保留。
  最大化时窗口对齐**显示器工作区**（不会比工作区高出一条、被任务栏盖住底部的状态栏）。
- 状态栏左端是提示，**右端**是图鉴后台加载的静默进度（如 `图鉴加载 240/341`，加载完自动消失）。
- 详情栏里的 **属性数值表** 把 Lv.1 / Lv.99 / 成长差值 按列对齐（列宽用 `Grid.IsSharedSizeScope` 共享）。
- 详情栏的「可继承技」是**一行一个技能**，右边对齐显示这只数码兽学会它的等级（如 `Lv12`）；
  `data.csv` 里没写等级的条目显示 `Lv?`（灰字 + 悬停提示），下方附一行脚注说明缺失条数。
- 中间路线图是**蛇形排布**（`view/Controls/RouteChain.cs`，替代原先的 `WrapPanel`）：一行放不下就换行，但下一行**反向**排列，
  上一行最右那只是一个接一个往左排，于是它的下一只正好落在它正下方，两者之间用竖向箭头（↓）连接；
  反向行内部的箭头换成镜像字形（↗ → ↖、↘ → ↙）。
  **每条箭头是两行：上面一行是方向箭头，下面一行直接写语义**（进化 / 退化 / 形态转换 / 回落，颜色同箭头），
  不用再靠方向或颜色去猜是进化还是退化；悬停另有「从谁到谁」的完整提示。
  反向行用的是**换字形**而不是给整块做水平镜像——镜像会把下面那行文字照成反字。
  卡片本身是**横向条**（208×78，和图鉴关系网的节点卡同一套语言），左侧那条色条就是 `起` / `终` / `途`。
- 在左侧多选了下「需沿途收集的继承技」后，算出的每条路线会：**卡片上**给能提供该技能的数码兽加一枚紫色小标
  （技能名 + 该数码兽学会它的等级，卡片上只占一行），**路线标题下**加一行「沿途收集：技能（等级 · #编号 名字）」。
  `data.csv` 里没写等级的显示 `Lv?`。
- 「文本视图」显示与命令行完全一致的文本（含箭头 `↗ ↘ ⮂ →`），可「导出」成 txt。

---

## 三个输入框（起点 / 终点 / 图鉴筛选）

三处**共用同一套输入与匹配逻辑**：

- 输入处理：`SuggestBox`（起点 / 终点）与直接双向绑定（图鉴筛选框）都**以输入框自己的文本为准**去回调，
  再把文本写回 VM 属性（`UpdateSourceTrigger=PropertyChanged`）。`SuggestBox` 内部在 `TextChanged` 里
  先取输入框文本、再调 `SuggestionProvider`，不读绑定到 VM 的属性——TwoWay 绑定的回写时机在 `TextChanged` 之后，
  读 VM 会拿到上一次的旧文本，表现为「输入不生效 / 改成能匹配的字反而 0 条 / 清空不还原」。
- 匹配逻辑：`Core/InputMatcher.Suggest`——字面（名称完全一致 1.0、开头 0.95、包含 0.85；别名同；编号 0.9；世代 0.8）
  ＋ 拼音包含（`FuzzyMatcher.MatchPinyin`，0.4–0.9）＋ 错字容错（hybrid 得分），按匹配度从高到低，
  起点/终点取前 24 条，图鉴筛选取前 400 条候选再按门槛过滤（条数与门槛见 config.json）。
- 起点 / 终点是 `view/Controls/SuggestBox`（输入框 + `Popup` 候选列表，**名称在左、匹配度在右**）：
  输入即出候选，↑↓ 选择、回车/鼠标点击填入（填入会再走一遍输入处理，提示随即刷新）、Esc 关闭。
  它是自定义控件，候选来自绑定（`SuggestionProvider="{Binding SuggestStart}"`），页面里没有事件处理器。
  离屏截图只保存主图：Popup 不在窗口的可视树里，抓不进去。
- 注意：输入框下面那行提示仍是**求解器的解析结果**（与 `py/planner.py` 的匹配行为对齐，分数口径不同）——
  比如输入 `jixie`，planner 会解析成弱匹配的「独角兽」而下拉框把「机械暴龙兽」排在前面，
  下拉框的作用就是帮你挑一个准确的名字填进去。

---

## 图鉴（左上角「图鉴」页签）

标题栏左上角可以切换 **路线 / 图鉴**两种界面：

- 进图鉴后**左侧换成图鉴瀑布流**：`ItemsControl` + `VirtualizingMasonryPanel`（`view/Controls/VirtualizingMasonryPanel.cs`，
  自写的虚拟化面板 + `IScrollInfo`，只给视口内的卡片建容器；341 只实测只实体化 ~16 个），
  列数随面板宽度自动变（默认左栏宽下 2–3 列）；筛选框支持名字 / 别名 / 编号 / 世代，也能拼音或错字。
  点卡片就是「以它为中心」（中心那张描边变强调色 + 顶色条）。
  筛选的关键字**以输入框文本为准**（`CodexFilterBox.Text`）：不去读绑定到 VM 的那个值，
  绑定（尤其带 Delay 时）的更新时机在 `TextChanged` 之后，会拿到上一次的旧关键字，
  表现出来就是「输入的字符没生效 / 改回有匹配的关键字反而 0 条 / 清空不回初始」。
  匹配顺序：字面（名字/别名/编号/世代）→ 一个都没命中时才兜底（拼音包含 `MatchPinyin`，再错字容错）。
  结果是**按匹配度过滤**的，门槛见 config.json 的 `codexFilterMinScore`（默认 0.7）：低于它的候选不显示，
  所以 `jixie` 只会留下机械暴龙兽等 5 只，不会把 13% 那种弱匹配（独角兽等）也列出来；改成 0 就能看到全部命中项。
  注意：筛选是**整份替换** `CodexEntries`（`IReadOnlyList`）而不是往 `ObservableCollection` 里逐条 `Add`——
  逐条 Add 会让面板对每项走一次容器生成（卡顿），而且清空搜索框后容易与生成器状态错开（列表回不到初始状态）。
- **卡片规格**：图鉴列表是竖版小卡（头像为主），**关系网节点与路线卡片都是横向条**
  （头像在左、名字与 `#编号 · 世代` 在右）——一层占的高度大约是竖版的三分之一，这是能一屏看全中心周围的关键。
  完整卡片尺寸写死、名字固定占两行（**不省略号**，长了就换行，所以所有卡片等高）；
  头像裁进圆角框（原图底色各不相同，裁圆之后风格才一致）；`中心` / `路线`（关系网）或 `起` / `终` / `途`（路线）
  的徽标占名字行右端，名字自然让位；悬停给一圈强调色描边 + 柔和投影。
  模板见 `IllustratedGuideView.xaml` 的 `GuideCard` / `CodexNode` / `CodexNodeCompact` 与
  `EvolutionRouteView.xaml` 的 `RouteCard`。
- 中间的关系网按「离中心几步」分层向两侧摆（**深度** 1–4），节点太多时只展开前 160 只并在图上提示。
- 方向是**单侧单方向**的：中心**左边**只沿「退化」展开（能退化到的形态），**右边**只沿「进化」展开（能进化成的形态）。
  比如 a 能进化成 b，那么从 b 继续往下只看 b 的进化，b 的退化对象不进图。
- **外层收成窄条**：**第 2 级及以后、并且这一层数量 > 6** 的，平时收成一条窄条
  （**小头像 + `#编号` + 名字**）并淡出——前一个条件保证中心那圈直接邻居始终完整，
  后一个条件保证只有真正挤的那几层才收。亚古兽各层数量是 L1=6 / L2=18 / L3=41，
  于是深度 1 的第 1 层完整、深度 2 的第 2 层收、深度 3 的第 2 与第 3 层都收。
  阈值在 `Core/Presenter.cs` 的 `CompactFromLevel` / `CompactMinCount`；
  两个模板是 `CodexNode`（完整 210×66）与 `CodexNodeCompact`（窄条 210×34）。
- **悬停 / 点选不放大，只点亮**：被指的、点选钉住的，以及**与它们直接相连**的节点一起点亮
  （`Opacity` 从 0.38 提到 1），相连的连线也变实线；其余窄条仍是灰的。
  点选钉住的那个再多套一圈强调色描边（`CodexGraph.OnRender` / `PinRing`），和「只是鼠标扫过」区分开。
  之所以不做「悬停放大」：窄条之间只有 42px 间距而完整卡片高 66px，多张一起放大会互相遮住。
- **布局**（`CodexGraph.LayOut`）围绕「关联的卡片尽量靠得近、线尽量不压到卡片上」：
  1. 按 `Level` 分列；
  2. 列内反复按「邻居所在行号的平均值」重排（来回扫 4 轮，交叉明显减少）；
  3. 每个节点往邻居的平均高度靠——用 PAVA 做保序最小二乘拟合，保证列内顺序与最小间距的同时最贴近邻居，
     整列不会被单侧约束带上偏（`FitColumn`）。拟合按**逐节点高度**累加，所以完整卡片与窄条能混在一列里；
  4. 每条连线在卡片边缘有自己的落点：同一个节点同一侧的连线按对端高度排成扇面，不会全挤在一个点上。
- 连线：绿 = 进化，橙 = 退化，紫虚线 = 同级／形态关系；一律**三次贝塞尔、水平出入**（节点编辑器那种 S 形），
  所以横向连接的线严格待在两列之间，不会扫过别的卡片；
  同列的形态关系改成**往列外侧鼓一下**（偏移量按「刚好越过卡片半个宽度、又留在列间空隙里」算），
  于是也不会从中间那些卡片身上穿过去。
  「高亮已算出的路线」打开时，最近一次计算出的路线经过的连线和节点会被加粗、标上紫色「路线」标。
- **明暗层次**：只有从中心出发的那一圈是实线，第 2 级往后先淡出（收成窄条的也一样淡，它们只是远处的背景）。
  **悬停**某个节点时，它、与它直接相连的节点、以及这些连线一起点亮；
  **单击**会把高亮**钉住**（节点外面多套一圈强调色描边），一直保持到点空白处或点别的节点为止
  ——拖动平移（按下后挪动超过阈值）不算点击，不会把拖过的卡片钉住（见 `SetHover` / `SetPin` / `OnRender`）。
- 节点单击 = 看右侧详情（与路线卡片一致），**双击 = 以它为中心重新展开**；
  工具条的「回到中心」把中心挪回视口正中（换中心时也会自动居中）。- 图形可以直接**拖动平移**（按住左键拖，拖动时鼠标变十字），四周留 360px 空白，
  所以拖动范围比关系网本身大一圈（不会刚好卡在内容边界上）；**滚动条按需求隐藏**，滚动能力都保留。
- **缩放**：工具条的 `− 100% ＋`，或按住 **Ctrl + 滚轮**无级缩放（40%–200%），以光标处为锚点缩放；
  已经到 40% / 200% 时这一下滚轮会被吃掉，不会「回落」成纵向滚动。
- 滚轮：**Shift + 滚轮 = 横向滚动**，其余情况是纵向滚动。
- 切到路线再切回来**缩放和视口都不会被重置**（只有第一次进入、换中心、点「回到中心」才居中）。
- 图鉴节点和列表都用 **160px 缩略图**（`ImageStore.GetThumbnail`）并按需解码。
  **开软件时就在后台异步加载图鉴**（`MainViewModel.WarmUpGuideAsync`，分块建卡片，进度静默写在状态栏右下角），
  所以第一次点进图鉴页通常是一闪而过；真要等的时候才盖一层「正在生成图鉴…」遮罩。

### 图片

把 `{编号}.png` 放进下面任一目录即可自动显示（`18.png` → 亚古兽）：

1. `--assets` 指定的目录（或 `app.settings.json` 里上次记住的那个）
2. exe 同级的 `imgs/`、`assets/`
3. 从 exe 目录向上若干级的 `imgs/`、`assets/`（开发时把图放在仓库的 `dotnet/imgs/` 就能直接被找到）

找不到图片时卡片和详情栏画虚线占位框。`dotnet/imgs/` 与 `dotnet/assets/*.png` 已在 `.gitignore` 里（几百 MB，属于本机资源）。

---

## 可调参数（config.json）

阈值、上限、默认尺寸都集中在 `dotnet/config.json`（读它的是 `Core/AppConfig.cs`）：

| 分组 | 内容 |
| --- | --- |
| `search` | 路径条数默认值/上限、起点终点候选条数、图鉴筛选的条数与**匹配度门槛**（默认 0.7）、提示里「完全匹配 / 相似度」的分数 |
| `codex` | 默认/最大深度、关系网节点上限、缩放范围与步进、拖动阈值、双击判定窗口、四周留白、层间距、缩略图宽度 |
| `layout` | 窗口默认与最小尺寸、左右栏默认宽与最小值 |

- 允许写 `//` 注释和尾随逗号；删掉某个字段就用 `Core/AppConfig.cs` 里的默认值。
- 程序**从 exe 目录往上**找第一份 `config.json`：所以改 `dotnet/config.json` 后直接重跑 exe 就行（不必重新构建；exe 同级那份是构建时复制的）。
  日志里会写实际加载了哪一份（`[config] 已加载 …`）。
- 用户状态（上次的数据文件、图片目录、窗口与栏宽）不在 config.json 里，在 `app.settings.json`。

---

## 命令行开关

| 开关 | 说明 |
| --- | --- |
| `--data <csv>` | 指定数据文件 |
| `--assets <dir>` | 指定图片目录 |
| `--start` / `--end` / `--k` | 启动时预填表单 |
| `--skill <技能名>` | 启动时预选继承技（可重复写多个） |
| `--codex` / `--depth <1-4>` / `--codex-filter <文本>` | 启动（截图）时直接进图鉴模式、指定展开深度与左侧筛选
| `--window 1600x1000` | 初始窗口尺寸 |
| `--maximized` | 启动时直接最大化（截图回归里用来验证对齐工作区） |
| `--zoom <百分比>` | 图鉴关系网的初始缩放（默认 100；截图回归里用来验证「回到中心」） |
| `--cli <参数...>` | 无界面执行一次查询，参数写法与 `planner.py` 一致（例：`--cli 亚古兽 战斗暴龙兽 -k 3`） |
| `--out <file>` | 配合 `--cli` 把输出写到文件（默认写标准输出） |
| `--screenshot <png>` | 离屏渲染界面截图后退出（回归验收用；Popup 弹层不在可视树里，只保存主图） |
| `--log <file>` | 把诊断日志写到文件 |

---

## 与原 Python 实现的关系

`../py/planner.py` 保持原样（分支上未做任何内容改动），作为命令行版继续可用；
本工程的 C# 实现与它**逐条对拍过 15 组查询**（基础/多终点/任意终点+途经/继承技/排除世代/翻转/编号/错误参数等），
路线文本逐字一致。唯一一处刻意偏差：

- 继承技里的 ASCII 罗马数字：原实现因正则候选顺序会把 `II` 当成 `1`（`云雾汽油弹II` → 匹配到「云雾汽油弹**1**」），
  这里按文档本意实现成 2（`II`/`ii` → `2`，`III` → `3`）。Unicode 的 `Ⅱ` 两边行为一致。
