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
    }

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
