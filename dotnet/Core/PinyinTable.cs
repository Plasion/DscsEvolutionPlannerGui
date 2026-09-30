using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DscsEvolutionPlanner.Core;

/// <summary>
/// 字符→拼音数据表（由开发期脚本 tools/gen_pinyin_table.py 生成后随程序发布）。
/// 运行时不需要 Python：这里只读一份 JSON。
/// </summary>
public sealed class PinyinTable
{
    private const int MaxPhraseLength = 12;

    private Dictionary<string, string[]> _phrases = new();
    private Dictionary<string, string> _chars = new();
    private List<string> _syllables = new();
    private HashSet<string> _syllableSet = new();

    public bool IsLoaded { get; private init; }
    public string KeyHash { get; private init; } = "";
    public string Source { get; private init; } = "";
    public int SyllableCount => _syllables.Count;

    public static PinyinTable Empty { get; } = new();

    private sealed class Payload
    {
        public int version { get; set; }
        public string? keyHash { get; set; }
        public string? source { get; set; }
        public Dictionary<string, string[]>? phrases { get; set; }
        public Dictionary<string, string>? chars { get; set; }
        public List<string>? syllables { get; set; }
    }

    public static PinyinTable Load(string path)
    {
        if (!File.Exists(path)) return Empty;
        var payload = JsonSerializer.Deserialize<Payload>(
            File.ReadAllText(path, Encoding.UTF8),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (payload is null) return Empty;

        var table = new PinyinTable
        {
            _phrases = payload.phrases ?? new Dictionary<string, string[]>(),
            _chars = payload.chars ?? new Dictionary<string, string>(),
            _syllables = (payload.syllables ?? new List<string>()).OrderByDescending(s => s.Length).ToList(),
            IsLoaded = true,
            KeyHash = payload.keyHash ?? "",
            Source = payload.source ?? path,
        };
        table._syllableSet = new HashSet<string>(table._syllables, StringComparer.Ordinal);
        return table;
    }

    /// <summary>词典里某个键（名字/别名/世代/技能名）的整词拼音。</summary>
    public string[] ForKey(string key)
    {
        if (_phrases.TryGetValue(key, out var syllables)) return syllables;
        return Convert(key);
    }

    /// <summary>把任意输入转成拼音音节序列：整词优先，其次逐字，纯字母输入按音节表切分。</summary>
    public string[] Convert(string text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        if (IsAsciiLetters(text)) return SplitPinyin(text);

        var result = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var matched = false;
            if (_phrases.Count > 0)
            {
                var max = Math.Min(MaxPhraseLength, text.Length - i);
                for (var len = max; len >= 2; len--)
                {
                    if (!_phrases.TryGetValue(text.Substring(i, len), out var phrase)) continue;
                    result.AddRange(phrase);
                    i += len;
                    matched = true;
                    break;
                }
            }
            if (matched) continue;

            var ch = text[i].ToString();
            result.Add(_chars.TryGetValue(ch, out var syllable) && syllable.Length > 0 ? syllable : ch);
            i++;
        }
        return result.ToArray();
    }

    private static bool IsAsciiLetters(string text)
    {
        foreach (var ch in text)
            if (!((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')))
                return false;
        return text.Length > 0;
    }

    /// <summary>纯拼音输入切分：贪心匹配最长音节（与 planner.py 用的 mohu 行为一致）。</summary>
    private string[] SplitPinyin(string text)
    {
        var lower = text.ToLowerInvariant();
        var result = new List<string>();
        var i = 0;
        while (i < lower.Length)
        {
            var matched = false;
            if (_syllables.Count > 0)
            {
                foreach (var syllable in _syllables)
                {
                    if (syllable.Length > lower.Length - i) continue;
                    if (!lower.AsSpan(i).StartsWith(syllable, StringComparison.Ordinal)) continue;
                    result.Add(syllable);
                    i += syllable.Length;
                    matched = true;
                    break;
                }
            }
            if (matched) continue;
            result.Add(lower[i].ToString());
            i++;
        }
        return result.ToArray();
    }

    /// <summary>与生成脚本一致的 FNV-1a 32 位哈希，用来核对拼音表是否与当前数据同源。</summary>
    public static string ComputeKeyHash(IEnumerable<string> keys)
    {
        var payload = string.Join("\n", keys.OrderBy(k => k, StringComparer.Ordinal));
        var hash = 0x811C9DC5u;
        foreach (var b in Encoding.UTF8.GetBytes(payload))
            hash = (hash ^ b) * 0x01000193u;
        return hash.ToString("x8");
    }
}
