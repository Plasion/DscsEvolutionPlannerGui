using System;
using System.Collections.Generic;
using System.Linq;

namespace DscsEvolutionPlanner.Core;

/// <summary>一次匹配命中的候选。</summary>
public sealed class MatchHit
{
    public string Key { get; init; } = "";
    public double CharScore { get; init; }
    public double PinyinScore { get; init; }

    /// <summary>与 mohu 的 hybrid 模式一致：字符 0.6 + 拼音 0.4。</summary>
    public double Score => 0.6 * CharScore + 0.4 * PinyinScore;
}

/// <summary>模糊匹配（与原 mohu 对齐）：编辑距离 ≤ maxDistance 召回，得分 = 0.6 × 字符分 + 0.4 × 拼音分。</summary>
public sealed class FuzzyMatcher
{
    private readonly List<Entry> _entries = new();
    private readonly PinyinTable _pinyin;
    private readonly int _maxDistance;

    private sealed class Entry
    {
        public string Key = "";
        public string[] Chars = Array.Empty<string>();
        public string[] Pinyin = Array.Empty<string>();
    }

    public FuzzyMatcher(IEnumerable<string> keys, PinyinTable pinyin, int maxDistance = 2)
    {
        _pinyin = pinyin;
        _maxDistance = maxDistance;
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key)) continue;
            _entries.Add(new Entry
            {
                Key = key,
                Chars = key.Select(c => c.ToString()).ToArray(),
                Pinyin = pinyin.ForKey(key),
            });
        }
    }

    public int Count => _entries.Count;

    /// <summary>按得分从高到低返回候选（得分相同的保持词典顺序）。</summary>
    public IReadOnlyList<MatchHit> Match(string text, int maxResults = 3)
    {
        var value = text?.Trim() ?? "";
        if (value.Length == 0 || _entries.Count == 0) return Array.Empty<MatchHit>();

        var queryChars = value.Select(c => c.ToString()).ToArray();
        var queryPinyin = _pinyin.Convert(value);

        var hits = new List<MatchHit>();
        foreach (var entry in _entries)
        {
            var charScore = Score(queryChars, entry.Chars);
            var pinyinScore = Score(queryPinyin, entry.Pinyin);
            if (charScore <= 0 && pinyinScore <= 0) continue;
            hits.Add(new MatchHit { Key = entry.Key, CharScore = charScore, PinyinScore = pinyinScore });
        }

        // OrderByDescending 是稳定排序，得分并列时保留词典顺序（与 mohu 一致）
        var ordered = hits.OrderByDescending(hit => hit.Score).ToList();
        return maxResults > 0 && ordered.Count > maxResults ? ordered.Take(maxResults).ToList() : ordered;
    }

    /// <summary>取最佳候选（planner.py 的 find_digimon 就是取第一个，不做阈值过滤）。</summary>
    public MatchHit? Best(string text) => Match(text, 1).FirstOrDefault();

    /// <summary>
    /// 拼音包含匹配（专治「jixie」这种音节数对不上的查询——按编辑距离召不回 机械暴龙兽）。
    /// 分数 0.4–0.9，命中越靠前越高，供下拉排序用。
    /// </summary>
    public IReadOnlyList<MatchHit> MatchPinyin(string text, int maxResults = 40)
    {
        var value = text?.Trim() ?? "";
        if (value.Length == 0 || _entries.Count == 0) return Array.Empty<MatchHit>();

        var query = string.Concat(_pinyin.Convert(value));
        if (query.Length == 0) return Array.Empty<MatchHit>();

        var hits = new List<MatchHit>();
        foreach (var entry in _entries)
        {
            var pinyin = string.Concat(entry.Pinyin);
            var at = pinyin.IndexOf(query, StringComparison.Ordinal);
            if (at < 0) continue;

            // 命中越靠前越像：开头 0.9，越往后最低 0.4；两个分量取同一个值，最终 Score = quality
            var quality = 0.9 - 0.5 * at / Math.Max(1, pinyin.Length);
            hits.Add(new MatchHit { Key = entry.Key, CharScore = quality, PinyinScore = quality });
        }

        var ordered = hits.OrderByDescending(hit => hit.Score).ToList();
        return maxResults > 0 && ordered.Count > maxResults ? ordered.Take(maxResults).ToList() : ordered;
    }

    private double Score(string[] query, string[] candidate)
    {
        if (query.Length == 0 || candidate.Length == 0) return 0;
        var distance = Levenshtein(query, candidate);
        if (distance > _maxDistance) return 0;                       // 召回门槛
        var length = Math.Max(query.Length, candidate.Length);
        return Math.Max(0, 1.0 - (double)distance / length);
    }

    private static int Levenshtein(string[] a, string[] b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = string.Equals(a[i - 1], b[j - 1], StringComparison.Ordinal) ? 0 : 1;
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
