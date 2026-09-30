namespace DscsEvolutionPlanner.Core;

/// <summary>
/// 应用入口要用的启动编排：把命令行开关与上次保存的用户状态合成一份启动配置。
/// 界面（view）从这里拿到「这次该怎么启动」，不再自己拼装。
/// </summary>
public static class AppBootstrapper
{
    public static (StartupOptions Startup, UserState State) Prepare(CliOptions options)
    {
        var state = UserState.Load();
        var startup = new StartupOptions
        {
            DataFile = options.DataPath,
            ImageDirectory = options.AssetsPath ?? state.AssetsDirectory,
            WindowWidth = options.WindowWidth ?? state.WindowWidth,
            WindowHeight = options.WindowHeight ?? state.WindowHeight,
            LeftColumnWidth = state.LeftWidth,
            DetailsColumnWidth = state.DetailsWidth,
            K = options.K ?? state.LastK,
            StartText = options.StartText,
            EndText = options.EndText,
            SelectedSkills = options.Skills,
            DirectGuide = options.CodexMode,
            CodexDepth = options.CodexDepth,
            CodexFilter = options.CodexFilter,
        };
        return (startup, state);
    }
}