using System;
using System.Collections.Generic;
using System.Linq;

namespace DscsEvolutionPlanner.Core;

/// <summary>文本搜索的唯一入口：把字面、拼音包含与错字容错合成一份打分排名（起点/终点候选、图鉴筛选共用）。</summary>
public sealed class InputMatcher
{
    private readonly IReadOnlyDictionary<int, Digimon> _digimons;
    private readonly IReadOnlyDictionary<string, int> _nameToId;
    private readonly FuzzyMatcher _names;

    public InputMatcher(
        IReadOnlyDictionary<int, Digimon> digimons,
        IReadOnlyDictionary<string, int> nameToId,
        FuzzyMatcher names)
    {
        _digimons = digimons;
        _nameToId = nameToId;
        _names = names;
    }

    /// <summary>
    /// 候选（按匹配度从高到低，只保留匹配度 &gt; 0 的）：
    /// 字面命中权重最高，其次是拼音包含（jixie → 机械暴龙兽），最后才是错字容错的模糊分。
    /// </summary>
    public List<NameSuggestion> Suggest(string? text, int limit = 24)
    {
        var keyword = (text ?? "").Trim();
        var result = new List<NameSuggestion>();
        if (keyword.Length == 0) return result;

        var scores = new Dictionary<int, double>();

        void Keep(int id, double score)
        {
            if (score <= 0 || !_digimons.ContainsKey(id)) return;
            if (scores.TryGetValue(id, out var old) && old >= score) return;
            scores[id] = score;
        }

        foreach (var digimon in _digimons.Values)
        {
            var literal = LiteralScore(digimon, keyword);
            if (literal > 0) Keep(digimon.Id, literal);
        }

        foreach (var hit in _names.MatchPinyin(keyword, limit * 2))
            if (_nameToId.TryGetValue(hit.Key, out var id)) Keep(id, hit.Score);

        foreach (var hit in _names.Match(keyword, limit * 2))
            if (_nameToId.TryGetValue(hit.Key, out var id)) Keep(id, hit.Score);

        foreach (var pair in scores.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).Take(limit))
        {
            var digimon = _digimons[pair.Key];
            result.Add(new NameSuggestion(digimon.Id, digimon.Name, digimon.DisplayGeneration, pair.Value));
        }

        return result;
    }

    /// <summary>字面匹配的匹配度：名称完全一致 1.0、开头 0.95、包含 0.85；别名同；编号 0.9、世代 0.8。</summary>
    public static double LiteralScore(Digimon digimon, string keyword)
    {
        if (string.Equals(digimon.Name, keyword, StringComparison.OrdinalIgnoreCase)) return 1.0;
        if (digimon.Name.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)) return 0.95;
        if (digimon.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return 0.85;

        if (digimon.Alias is { Length: > 0 } alias)
        {
            if (string.Equals(alias, keyword, StringComparison.OrdinalIgnoreCase)) return 1.0;
            if (alias.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return 0.85;
        }

        if (digimon.Id.ToString().Contains(keyword, StringComparison.Ordinal)) return 0.9;
        if (digimon.Generation.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return 0.8;
        return 0;
    }
}
