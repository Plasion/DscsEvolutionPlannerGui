using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace DscsEvolutionPlanner.Core;

/// <summary>
/// 图片查找与缓存：找 <c>{编号}.png</c>（其次 <c>{名字}.png</c>），找不到返回 null 由界面画占位框。
/// 查找目录：用户指定目录 → exe 同级的 imgs/assets → 向上若干级的 imgs/assets。
/// </summary>
public sealed class ImageStore
{
    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" };

    private readonly Dictionary<int, BitmapSource?> _cache = new();
    private readonly Dictionary<int, BitmapSource?> _thumbCache = new();

    /// <summary>缩略图既会在后台预热（启动时的异步图鉴加载）里取，也会在界面上取，缓存要加锁。</summary>
    private readonly object _gate = new();

    /// <summary>界面上可配置的目录（空则只用默认查找路径）。</summary>
    public string Directory { get; set; } = "";

    public void Clear()
    {
        lock (_gate)
        {
            _cache.Clear();
            _thumbCache.Clear();
        }
    }

    public BitmapSource? Get(int id, string name)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(id, out var cached)) return cached;
            var image = TryLoad(id, name, 0);
            _cache[id] = image;
            return image;
        }
    }

    /// <summary>缩略图（图鉴列表 / 关系网用）：按宽度解码，比原图快且省内存。宽度见 config.json。</summary>
    public BitmapSource? GetThumbnail(int id, string name, int decodeWidth = 0)
    {
        if (decodeWidth <= 0) decodeWidth = (int)AppSettings.Current().ThumbnailWidth;
        lock (_gate)
        {
            if (_thumbCache.TryGetValue(id, out var cached)) return cached;
            var image = TryLoad(id, name, decodeWidth);
            _thumbCache[id] = image;
            return image;
        }
    }

    private BitmapSource? TryLoad(int id, string name, int decodeWidth)
    {
        var stems = new[] { id.ToString(), name }.Where(stem => !string.IsNullOrWhiteSpace(stem)).ToArray();
        foreach (var directory in SearchDirectories(Directory))
        {
            foreach (var stem in stems)
            {
                foreach (var extension in Extensions)
                {
                    var path = Path.Combine(directory, stem + extension);
                    var image = TryRead(path, decodeWidth);
                    if (image is not null) return image;
                }
            }
        }
        return null;
    }

    private static BitmapSource? TryRead(string path, int decodeWidth)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;   // 读完即释放文件
            if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;   // 缩略图：解码时就降采样
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();                                 // 冻结后可跨线程/绑定安全使用
            return image;
        }
        catch (Exception)
        {
            return null;   // 文件损坏、缺 WebP 解码器等：当作没有图片
        }
    }

    /// <summary>按顺序列出所有会被查找的图片目录（去重）。</summary>
    public static IReadOnlyList<string> SearchDirectories(string? configured = null)
    {
        var directories = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var full = path.Trim();
            if (directories.Any(existing => string.Equals(existing, full, StringComparison.OrdinalIgnoreCase))) return;
            directories.Add(full);
        }

        Add(configured);
        Add(Path.Combine(AppContext.BaseDirectory, "imgs"));
        Add(Path.Combine(AppContext.BaseDirectory, "assets"));

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 5 && directory is not null; i++, directory = directory.Parent)
        {
            Add(Path.Combine(directory.FullName, "imgs"));
            Add(Path.Combine(directory.FullName, "assets"));
        }

        return directories;
    }

    /// <summary>默认图片目录：优先挑一个已经放了图片的候选目录，否则用 exe 同级的 assets。</summary>
    public static string DefaultDirectory()
    {
        var candidates = SearchDirectories();
        var withImages = candidates.FirstOrDefault(HasAnyImage);
        return withImages ?? Path.Combine(AppContext.BaseDirectory, "assets");
    }

    private static bool HasAnyImage(string directory)
    {
        try
        {
            return System.IO.Directory.Exists(directory) && System.IO.Directory.EnumerateFiles(directory).Any();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
