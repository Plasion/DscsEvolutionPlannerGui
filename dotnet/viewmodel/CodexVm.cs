using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace DscsEvolutionPlanner.ViewModel;

/// <summary>左侧图鉴列表里的一张卡片（头像 + 名字 + 编号）。</summary>
public sealed class CodexEntryVm : NotifyBase
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string IdText => "#" + Id;

    /// <summary>缩略图：由核心层在生成卡片时一次性取好（虚拟化面板只实体化视口内的卡片）。</summary>
    public BitmapSource? Image { get; init; }
    public bool HasImage => Image is not null;

    public string Tip { get; init; } = "";

    private bool _isCenter;

    /// <summary>是不是当前图鉴中心（描边变强调色 + 顶上来一条色条）。</summary>
    public bool IsCenter
    {
        get => _isCenter;
        set
        {
            if (!Set(ref _isCenter, value)) return;
            Raise(nameof(HasCenterMark));
            Raise(nameof(BorderWidth));
        }
    }

    /// <summary>是否画顶部色条（绑定用，颜色由视图给）。</summary>
    public bool HasCenterMark => _isCenter;

    public double BorderWidth => _isCenter ? 1.6 : 1;
}

/// <summary>图鉴（网状图）里的一个节点 = 一只数码兽。</summary>
public sealed class CodexNodeVm
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Generation { get; init; } = "";
    public string IdText => "#" + Id;

    /// <summary>距中心的层数（0 = 中心；负数在中心左侧）。</summary>
    public int Level { get; init; }
    public bool IsRoot { get; init; }

    /// <summary>中心节点用强调色描边，其它节点普通边框（主题资源键）。</summary>
    public string BorderKey { get; init; } = "";
    public double BorderWidth { get; init; } = 1;

    public BitmapSource? Image { get; init; }
    public bool HasImage => Image is not null;

    /// <summary>最近一次算出的路线经过这里时高亮。</summary>
    public bool OnRoute { get; init; }

    public string Tip { get; init; } = "";
}

/// <summary>图鉴里的一条连线（Nodes 里的下标）。</summary>
public sealed class CodexLinkVm
{
    public int From { get; init; }
    public int To { get; init; }

    /// <summary>线色语义：进化 / 退化 / 形态（画线时换成主题资源）。</summary>
    public string Key { get; init; } = "";

    /// <summary>同级／形态关系用虚线区分。</summary>
    public bool Dashed { get; init; }

    /// <summary>最近一次算出的路线用到了这条连线。</summary>
    public bool OnRoute { get; init; }
}

/// <summary>图鉴视图的全部内容（核心层生成，CodexGraph 只负责摆位置和画线）。</summary>
public sealed class CodexGraphVm
{
    public List<CodexNodeVm> Nodes { get; init; } = new();
    public List<CodexLinkVm> Links { get; init; } = new();

    /// <summary>节点太多被截断时的提醒（空 = 没截断）。</summary>
    public string TruncatedNote { get; init; } = "";
    public bool IsTruncated => TruncatedNote.Length > 0;
}