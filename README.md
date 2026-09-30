# DscsEvolutionPlanner

《数码兽物语 网络侦探》进化路线规划器。仓库里有两套**互相独立**的实现，共用同一份数据：

| 目录 | 是什么 | 需要什么环境 |
| --- | --- | --- |
| [`py/`](py/) | 命令行版，算法本体（`planner.py`） | Python 3.10+ 与 `pip install mohu` |
| [`dotnet/`](dotnet/) | Windows 桌面界面（WPF），算法用 C# 重写了一份 | 只要 .NET 10 SDK，**不需要 Python** |

桌面版不启动任何子进程：它自己读 `data.csv`、自己做模糊匹配与路径搜索，所以装好 .NET 就能直接跑。

## 目录结构

```
DscsEvolutionPlanner/            ← 仓库根（.git 在这一层）
├─ py/                           ← Python 命令行版（算法本体，保持原样）
│  ├─ planner.py                 进化路线搜索 + 模糊匹配 + 命令行界面
│  ├─ data.csv                   341 只数码兽的图鉴与进退化数据（两套实现共用的唯一数据源）
│  └─ README.md                  命令行用法
└─ dotnet/                       ← .NET 桌面界面
   ├─ DscsEvolutionPlanner.slnx  解决方案（Visual Studio 直接打开）
   ├─ App.xaml(.cs)              应用入口 + 全部主题资源（画刷 / 样式 / 转换器）
   ├─ view/                      **界面层**：ShellView（底层壳）、EvolutionRouteView（进化线计算器）、
   │                             IllustratedGuideView（图鉴）三个视图；
   │                             Controls/ 里是自绘与自定义控件（关系网、蛇形链、虚拟化瀑布流、候选输入框、多选下拉）
   ├─ viewmodel/                 绑定层：MainViewModel（壳的状态与交互）、卡片 / 节点 / 详情 VM、Command 与对话框抽象
   ├─ Core/                      数据、算法与核心服务：EvolutionPlanner / PathFinder / FuzzyMatcher / DigimonCatalog /
   │                             PinyinTable / InputMatcher / AppConfig 与 PlannerFacade（核心 → VM 的唯一出口）
   ├─ config.json                可调参数（阈值 / 上限 / 默认尺寸）
   ├─ tools/gen_pinyin_table.py  开发期生成拼音表（唯一用到 Python 的地方，不是运行时依赖）
   ├─ Data/PinyinTable.json      拼音表（随程序发布）
   ├─ imgs/、assets/             数码兽图片（按编号命名，已在 .gitignore 里）
   └─ README.md                  界面用法、算法结构、可调参数
```

工程按 **view / viewmodel / Core** 三层划分，全工程只有 **4 个 XAML**：`App.xaml`（主题）+ 上面三个视图。
视图只通过 DataContext 与绑定和视图模型交互（XAML 里没有事件处理器），视图模型只通过属性与方法访问 Core。

## 快速开始

**命令行（Python 版）**

```powershell
cd py
pip install mohu
python planner.py 亚古兽 战斗暴龙兽 -k 3
```

**桌面版（WPF）**

```powershell
cd dotnet
dotnet run
```

界面里填起点/终点，点「计算路线」即可；也可以无界面跑一次：

```powershell
dotnet run -- --cli 亚古兽 战斗暴龙兽 -k 3
```

## 数据与一致性

- `py/data.csv` 是唯一数据源，`dotnet` 构建时把它复制到输出目录。
- 两套实现对拍过 15 组查询（基础 / 多终点 / 任意终点+途经 / 继承技 / 排除世代 / 翻转显示 / 编号 / 错误参数等），
  生成的路线文本逐字一致；唯一的刻意偏差见 [dotnet/README.md](dotnet/README.md#与原-python-实现的关系)。
- 拼音表由 `dotnet/tools/gen_pinyin_table.py` 生成；数据改动后重新生成一次即可，启动时会校验是否同源。
- 桌面版的阈值与上限（图鉴筛选的匹配度门槛、关系网节点上限、缩放范围等）都在 `dotnet/config.json`，见 [dotnet/README.md](dotnet/README.md)。

细节见 [py/README.md](py/README.md) 与 [dotnet/README.md](dotnet/README.md)。
