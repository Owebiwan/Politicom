using System.Text.Json;
using System.Text.Json.Serialization;

namespace Politicom.Sim;

/// <summary>
/// What a technology does. Local effects (production, defense) apply to communes that hold it;
/// society-wide effects (drift, coordination, diffusion…) scale with the share of communes that hold it.
/// </summary>
public sealed class TechEffects
{
    public double FoodProduction { get; set; }
    public double GoodsProduction { get; set; }
    public double GoodsUpkeep { get; set; }
    public double Defense { get; set; }
    /// <summary>Fractional reduction of coordination cost (negative = decisions get slower).</summary>
    public double Coordination { get; set; }
    public double DriftPerTick { get; set; }
    public double DriftBrake { get; set; }
    public double TrustGrowth { get; set; }
    public double Diffusion { get; set; }
    public double Contagion { get; set; }
    public double FreeRiding { get; set; }
    public double PoliticalEducation { get; set; }
    public double Influence { get; set; }

    /// <summary>Plain-language effects, strengths first and costs last, e.g. "Food +15%", "Drift +0.06/turn".</summary>
    public IEnumerable<(string Text, bool Good)> Describe()
    {
        static string Pct(double v) => $"{(v > 0 ? "+" : "−")}{Math.Abs(v) * 100:0}%";
        if (FoodProduction != 0) yield return ($"Food {Pct(FoodProduction)} (holders)", FoodProduction > 0);
        if (GoodsProduction != 0) yield return ($"Goods {Pct(GoodsProduction)} (holders)", GoodsProduction > 0);
        if (Defense != 0) yield return ($"Defense {Pct(Defense)} (holders)", Defense > 0);
        if (PoliticalEducation != 0) yield return ($"Political schooling {Pct(PoliticalEducation)} (holders)", PoliticalEducation > 0);
        if (Coordination != 0) yield return ($"Coordination cost {Pct(-Coordination)}", Coordination > 0);
        if (TrustGrowth != 0) yield return ($"Trust from aid {Pct(TrustGrowth)}", TrustGrowth > 0);
        if (Diffusion != 0) yield return ($"Ideas spread {Pct(Diffusion)}", Diffusion > 0);
        if (Contagion != 0) yield return ($"Contagion across the lines {Pct(Contagion)}", Contagion > 0);
        if (FreeRiding != 0) yield return ($"Free-riding {Pct(-FreeRiding)}", FreeRiding > 0);
        if (Influence != 0) yield return ($"Influence {(Influence > 0 ? "+" : "−")}{Math.Abs(Influence):0.0}/turn", Influence > 0);
        if (DriftBrake != 0) yield return ($"Drift brake −{DriftBrake:0.00}/turn", true);
        if (GoodsUpkeep != 0) yield return ($"Goods upkeep +{GoodsUpkeep:0.00} per head (holders)", false);
        if (DriftPerTick != 0) yield return ($"Drift +{DriftPerTick:0.00}/turn", false);
    }
}

/// <summary>How strongly communes of a given character tend to discover a technology.</summary>
public sealed class TechAffinity
{
    public double Agrarian { get; set; }
    public double Industrial { get; set; }
    public double Militant { get; set; }
    public double Federalist { get; set; }
    public double Collectivist { get; set; }
}

public sealed class TechDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Perk { get; set; } = "";
    public string Risk { get; set; } = "";
    public string[] Prerequisites { get; set; } = Array.Empty<string>();
    public double MinPractical { get; set; }
    public double MinPolitical { get; set; }
    public TechAffinity Affinity { get; set; } = new();
    public TechEffects Effects { get; set; } = new();
}

public sealed class TechCatalog
{
    private readonly Dictionary<string, TechDefinition> _byId;

    public TechCatalog(IEnumerable<TechDefinition> techs)
    {
        All = techs.ToList();
        _byId = All.ToDictionary(t => t.Id);
        foreach (var t in All)
            foreach (var p in t.Prerequisites)
                if (!_byId.ContainsKey(p)) throw new InvalidDataException($"Tech '{t.Id}' requires unknown tech '{p}'.");
        foreach (var t in All) Depth(t.Id); // throws on a cycle
    }

    /// <summary>Categories in the order they appear in the data.</summary>
    public IReadOnlyList<string> Categories => All.Select(t => t.Category).Distinct().ToList();

    private readonly Dictionary<string, int> _depth = new();

    /// <summary>Length of the longest prerequisite chain leading to a technology (0 for a root).</summary>
    public int Depth(string id) => Depth(id, new HashSet<string>());

    private int Depth(string id, HashSet<string> visiting)
    {
        if (_depth.TryGetValue(id, out int known)) return known;
        if (!visiting.Add(id)) throw new InvalidDataException($"Technology prerequisites form a cycle through '{id}'.");
        int depth = _byId[id].Prerequisites.Select(p => Depth(p, visiting) + 1).DefaultIfEmpty(0).Max();
        visiting.Remove(id);
        return _depth[id] = depth;
    }

    /// <summary>Technologies that list this one as a prerequisite.</summary>
    public IEnumerable<TechDefinition> Unlocks(string id) => All.Where(t => t.Prerequisites.Contains(id));

    public IReadOnlyList<TechDefinition> All { get; }

    public TechDefinition this[string id] => _byId[id];

    public bool Contains(string id) => _byId.ContainsKey(id);

    public static TechCatalog Load(string path) => Parse(File.ReadAllText(path));

    public static TechCatalog Parse(string json)
    {
        var file = JsonSerializer.Deserialize<TechFile>(json, Json.Options)
                   ?? throw new InvalidDataException("Could not read the technologies file.");
        return new TechCatalog(file.Technologies);
    }

    private sealed class TechFile
    {
        public List<TechDefinition> Technologies { get; set; } = new();
    }
}

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
