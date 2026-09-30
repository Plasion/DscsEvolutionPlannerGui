using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DscsEvolutionPlanner.Core;

/// <summary>一只数码兽的可继承技（含学会等级）。</summary>
public sealed class InheritSkill
{
    public string Name { get; init; } = "";
    public int? Level { get; init; }
    public string LevelText => Level is { } level ? $"Lv{level}" : "";
    public string Display => Level is { } level ? $"{Name}（Lv{level}）" : Name;
    public override string ToString() => Display;
}

/// <summary>一只数码兽的全部数据（图鉴展示 + 搜索用的进退化/继承技）。</summary>
public sealed class Digimon
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? Alias { get; init; }
    public string Generation { get; init; } = "";
    public string Race { get; init; } = "";
    public string Attribute { get; init; } = "";
    public string Growth { get; init; } = "";
    public int Slot { get; init; }
    public int Capacity { get; init; }

    /// <summary>退化 / 进化（存编号，来源是 CSV 里的名字）。</summary>
    public List<int> Devolutions { get; } = new();
    public List<int> Evolutions { get; } = new();

    /// <summary>继承技的技能名（已去掉 "Lv5." 前缀、罗马数字已转阿拉伯数字）。</summary>
    public List<string> InheritSkillNames { get; } = new();
    public List<InheritSkill> InheritSkills { get; } = new();

    public string SsName { get; init; } = "";
    public string SsDesc { get; init; } = "";
    public string MoveName { get; init; } = "";
    public string MoveDesc { get; init; } = "";

    public Dictionary<string, string> Lv1 { get; init; } = new();
    public Dictionary<string, string> Lv99 { get; init; } = new();

    public string DisplayGeneration => Generation.Length == 0 ? "（未标注）" : Generation;
    public override string ToString() => $"[{Id}]{Name}";
}

public sealed class DataException : Exception
{
    public DataException(string message) : base(message) { }
}

/// <summary>
/// data.csv 的加载与索引（C# 版，等价 planner.py 的 load_data + bridge 的展示列解析）。
/// 只读数据文件，不依赖 Python。
/// </summary>
public sealed class DigimonCatalog
{
    private static readonly string[] StatOrder = { "HP", "SP", "ATK", "DEF", "INT", "SPD" };

    private readonly Dictionary<int, Digimon> _byId = new();
    private readonly Dictionary<int, List<int>> _graph = new();
    private readonly Dictionary<string, int> _nameToId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<int>> _generationToIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<int>> _skillToIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _skillMinLevel = new(StringComparer.Ordinal);

    public string SourcePath { get; private init; } = "";
    public string DataHash { get; private set; } = "";
    public string PinyinPath { get; private init; } = "";
    public bool PinyinLoaded { get; private init; }
    public bool PinyinStale { get; private set; }
    public List<string> Warnings { get; private set; } = new();

    public IReadOnlyDictionary<int, Digimon> ById => _byId;
    public IReadOnlyDictionary<int, List<int>> Graph => _graph;
    public IReadOnlyDictionary<string, int> NameToId => _nameToId;
    public IReadOnlyDictionary<string, List<int>> GenerationToIds => _generationToIds;
    public IReadOnlyDictionary<string, List<int>> SkillToIds => _skillToIds;

    /// <summary>每个继承技“最早能在多少级学到”（取全部数码兽里的最小值）。</summary>
    public IReadOnlyDictionary<string, int> SkillMinLevels => _skillMinLevel;

    public List<string> NameKeys { get; private set; } = new();
    public List<string> GenerationKeys { get; private set; } = new();
    public List<string> SkillKeys { get; private set; } = new();

    /// <summary>文本搜索（候选下拉 / 图鉴筛选）统一走它。</summary>
    public InputMatcher Matcher { get; private set; } = null!;

    public FuzzyMatcher NameMatcher { get; private set; } = null!;
    public FuzzyMatcher GenerationMatcher { get; private set; } = null!;
    public FuzzyMatcher SkillMatcher { get; private set; } = null!;

    // ------------------------------------------------------------------ 加载

    public static DigimonCatalog Load(string csvPath, string? pinyinPath = null)
    {
        if (!File.Exists(csvPath))
            throw new DataException($"找不到数据文件：{csvPath}");

        pinyinPath ??= Path.Combine(AppContext.BaseDirectory, "Data", "PinyinTable.json");
        var pinyin = PinyinTable.Load(pinyinPath);

        var rows = ReadCsv(csvPath);
        if (rows.Count == 0) throw new DataException($"数据文件是空的：{csvPath}");

        var header = rows[0];
        int Column(string name)
        {
            var index = Array.IndexOf(header, name);
            if (index < 0) throw new DataException($"数据文件缺少列「{name}」：{csvPath}");
            return index;
        }

        var colId = Column("编号");
        var colName = Column("名字");
        var colAlias = Column("别名");
        var colGeneration = Column("世代");
        var colRace = Column("种族");
        var colAttribute = Column("属性");
        var colSlot = Column("装备凹槽");
        var colCapacity = Column("消耗容量");
        var colGrowth = Column("成长");
        var colDevolve = Column("退化");
        var colEvolve = Column("进化");
        var colLv1 = Column("Lv.1数值");
        var colLv99 = Column("Lv.99数值");
        var colSs = Column("SS");
        var colMove = Column("必杀技");
        var colSkills = Column("继承技");

        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        var pending = new List<(Digimon Digimon, List<string> Devolve, List<string> Evolve)>();
        var catalog = new DigimonCatalog { SourcePath = csvPath, PinyinPath = pinyinPath, PinyinLoaded = pinyin.IsLoaded };

        foreach (var row in rows.Skip(1))
        {
            if (row.Length == 0 || row.All(string.IsNullOrWhiteSpace)) continue;
            string Cell(int index) => index < row.Length ? row[index].Trim() : "";

            var id = int.Parse(Cell(colId), CultureInfo.InvariantCulture);
            var alias = Cell(colAlias);
            var skills = QuotedList(Cell(colSkills))
                .Select(EvolutionPlanner.NormalizeSkillName)
                .Select(ParseInheritSkill)
                .Where(skill => skill is not null)
                .Select(skill => skill!)
                .ToList();

            var ss = QuotedList(Cell(colSs));
            var move = QuotedList(Cell(colMove));

            var digimon = new Digimon
            {
                Id = id,
                Name = Cell(colName),
                Alias = alias.Length == 0 ? null : alias,
                Generation = Cell(colGeneration),
                Race = Cell(colRace),
                Attribute = Cell(colAttribute),
                Growth = Cell(colGrowth),
                Slot = ParseInt(Cell(colSlot)),
                Capacity = ParseInt(Cell(colCapacity)),
                SsName = ss.Count > 0 ? ss[0] : "",
                SsDesc = ss.Count > 1 ? ss[1] : "",
                MoveName = move.Count > 0 ? move[0] : "",
                MoveDesc = move.Count > 1 ? move[1] : "",
                Lv1 = ParseStats(Cell(colLv1)),
                Lv99 = ParseStats(Cell(colLv99)),
            };
            digimon.InheritSkills.AddRange(skills);
            digimon.InheritSkillNames.AddRange(skills.Select(skill => skill.Name));

            if (!catalog._byId.TryAdd(id, digimon))
                throw new DataException($"数据文件里编号 {id} 重复。");
            byName[digimon.Name] = id;
            if (digimon.Alias is { Length: > 0 } aliasName) byName[aliasName] = id;

            pending.Add((digimon, QuotedList(Cell(colDevolve)), QuotedList(Cell(colEvolve))));
        }

        // 名字 → 编号（退化/进化列里写的是名字）
        foreach (var (digimon, devolve, evolve) in pending)
        {
            foreach (var name in devolve)
                digimon.Devolutions.Add(LookupId(byName, name, digimon));
            foreach (var name in evolve)
                digimon.Evolutions.Add(LookupId(byName, name, digimon));
        }

        // 双向图：先按「退化 + 进化」建边，再补全只写了一侧的关系（与 planner.py 行为一致）
        foreach (var digimon in catalog._byId.Values)
        {
            var neighbours = digimon.Devolutions.Concat(digimon.Evolutions).Distinct().ToList();
            catalog._graph[digimon.Id] = neighbours;
        }

        var completed = new List<int>();
        var seenCompleted = new HashSet<int>();
        foreach (var (id, neighbours) in catalog._graph)
        {
            foreach (var other in neighbours.ToList())
            {
                if (!catalog._graph.TryGetValue(other, out var otherNeighbours)) continue;
                if (otherNeighbours.Contains(id)) continue;
                otherNeighbours.Add(id);
                if (seenCompleted.Add(other)) completed.Add(other);
            }
        }

        var warnings = new List<string>();
        if (completed.Count > 0)
        {
            var names = string.Join("、", completed.Select(mid => $"[{mid}]{catalog._byId[mid].Name}"));
            warnings.Add($"警告：{names} 进退化表不完整，已临时进行补全");
        }

        // 世代 / 继承技 索引（保持 CSV 出现顺序，与 planner.py 的 dict 顺序一致）
        foreach (var digimon in catalog._byId.Values.OrderBy(d => d.Id))
        {
            if (!catalog._generationToIds.TryGetValue(digimon.Generation, out var generationIds))
                catalog._generationToIds[digimon.Generation] = generationIds = new List<int>();
            generationIds.Add(digimon.Id);

            foreach (var skill in digimon.InheritSkills)
            {
                if (!catalog._skillToIds.TryGetValue(skill.Name, out var skillIds))
                    catalog._skillToIds[skill.Name] = skillIds = new List<int>();
                if (!skillIds.Contains(digimon.Id)) skillIds.Add(digimon.Id);

                if (skill.Level is { } level
                    && (!catalog._skillMinLevel.TryGetValue(skill.Name, out var best) || level < best))
                    catalog._skillMinLevel[skill.Name] = level;
            }
        }

        catalog._nameToId.Clear();
        foreach (var pair in byName) catalog._nameToId[pair.Key] = pair.Value;

        catalog.NameKeys = byName.Keys.ToList();
        catalog.GenerationKeys = catalog._generationToIds.Keys.ToList();
        catalog.SkillKeys = catalog._skillToIds.Keys.ToList();

        catalog.NameMatcher = new FuzzyMatcher(catalog.NameKeys, pinyin);
        catalog.Matcher = new InputMatcher(catalog.ById, catalog.NameToId, catalog.NameMatcher);
        catalog.GenerationMatcher = new FuzzyMatcher(catalog.GenerationKeys, pinyin);
        catalog.SkillMatcher = new FuzzyMatcher(catalog.SkillKeys, pinyin);

        catalog.Warnings = warnings;
        catalog.DataHash = PinyinTable.ComputeKeyHash(
            catalog.NameKeys.Concat(catalog.GenerationKeys).Concat(catalog.SkillKeys));
        catalog.PinyinStale = catalog.PinyinLoaded && catalog.DataHash != pinyin.KeyHash;
        return catalog;
    }

    private static int LookupId(Dictionary<string, int> byName, string name, Digimon owner)
    {
        if (byName.TryGetValue(name, out var id)) return id;
        throw new DataException($"[{owner.Id}]{owner.Name} 的进退化表里写了「{name}」，但数据文件中没有这个名字。");
    }

    private static int ParseInt(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static Dictionary<string, string> ParseStats(string raw)
    {
        var stats = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in QuotedList(raw))
        {
            var index = item.IndexOf('：');
            if (index <= 0) continue;
            stats[item[..index].Trim()] = item[(index + 1)..].Trim();
        }
        return stats;
    }

    /// <summary>取出 Python 字面量里的单引号字符串："['a', 'b']" → ["a", "b"]。</summary></summary>
    /// <summary>把数据里的 "Lv5.物理耗竭" 拆成等级与技能名（没有 Lv 前缀时等级为空）。</summary>
    private static InheritSkill? ParseInheritSkill(string raw)
    {
        var dot = raw.IndexOf('.');
        if (dot < 0) return raw.Length > 0 ? new InheritSkill { Name = raw } : null;

        var name = raw[(dot + 1)..];
        if (name.Length == 0) return null;

        var levelText = raw[..dot];
        int? level = null;
        if (levelText.StartsWith("Lv", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(levelText[2..], out var parsed)) level = parsed;

        return new InheritSkill { Name = name, Level = level };
    }

    private static List<string> QuotedList(string text)
    {
        var result = new List<string>();
        var buffer = new StringBuilder();
        var inside = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\\' && inside && i + 1 < text.Length)
            {
                buffer.Append(text[++i]);
                continue;
            }
            if (ch == '\'')
            {
                if (inside) result.Add(buffer.ToString());
                buffer.Clear();
                inside = !inside;
            }
            else if (inside)
            {
                buffer.Append(ch);
            }
        }
        return result;
    }

    /// <summary>极简 CSV 读取：支持双引号包裹、引号内逗号与 "" 转义（data.csv 用到的就这些）。</summary>
    private static List<string[]> ReadCsv(string path)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        using var reader = new StreamReader(path, Encoding.UTF8);
        int next;
        while ((next = reader.Read()) >= 0)
        {
            var ch = (char)next;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    rows.Add(fields.ToArray());
                    fields.Clear();
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add(fields.ToArray());
        }
        return rows;
    }

    public static IReadOnlyList<string> Stats => StatOrder;
}
