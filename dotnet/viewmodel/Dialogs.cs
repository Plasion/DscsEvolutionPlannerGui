using System.Threading.Tasks;

namespace DscsEvolutionPlanner.ViewModel;

/// <summary>文件与提示框：视图模型只说「要做什么」，具体窗口由视图层实现（避免 VM 引用具体控件）。</summary>
public interface IDialogService
{
    /// <summary>挑一个目录；取消返回 null。</summary>
    string? PickFolder(string title, string? initial);

    /// <summary>挑一个数据文件；取消返回 null。</summary>
    string? PickDataFile(string? initial);

    /// <summary>把文本存到用户选的文件；返回实际路径（取消返回 null）。</summary>
    string? SaveText(string title, string suggestedName, string text);

    /// <summary>提示一条信息。</summary>
    void Warn(string title, string message);
}

/// <summary>窗口动作：视图模型不碰 Window，只发出请求。</summary>
public interface IWindowActions
{
    void Minimize();
    void ToggleMaximize();
    void Close();
}