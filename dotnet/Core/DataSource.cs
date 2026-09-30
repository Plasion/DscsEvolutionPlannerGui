using System.IO;

namespace DscsEvolutionPlanner.Core;

/// <summary>数据文件的来源：把「从哪读 data.csv」这件事收在一处，视图模型只问它。</summary>
public sealed class DataSource
{
    private readonly StartupOptions _options;

    public DataSource(StartupOptions options) => _options = options;

    /// <summary>按 --data → 上次记住的 → exe 同级 → 向上找 data.csv / py/data.csv 的顺序找。</summary>
    public string? Locate(string? remembered = null) =>
        CliQueryParser.ResolveDataPath(_options.DataFile, remembered);

    /// <summary>读取并建立索引；读不了就抛 <see cref="DataException"/>。</summary>
    public DigimonCatalog Read(string path, string? pinyinPath = null) =>
        DigimonCatalog.Load(path, pinyinPath);

    /// <summary>默认图片目录：优先挑一个已经放了图片的候选目录。</summary>
    public static string DefaultImageDirectory() => ImageStore.DefaultDirectory();

    /// <summary>尽力创建目录（失败就算了，界面只画占位框）。</summary>
    public static void EnsureDirectory(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path)) Directory.CreateDirectory(path.Trim());
        }
        catch (System.Exception) { /* 目录不可用就只画占位框 */ }
    }
}