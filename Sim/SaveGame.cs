using System.Text.Json;

namespace Politicom.Sim;

public sealed record TrustEdge(int A, int B, double Trust);

/// <summary>
/// Everything needed to resume a game exactly, including the RNG state, so a loaded game plays
/// out identically to one that was never saved. The scenario is embedded (it carries the live
/// enemy and ally); technology definitions are not, and come from the data files on load.
/// </summary>
public sealed class SaveGame
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public DateTime SavedAtUtc { get; set; }
    public ScenarioDefinition Scenario { get; set; } = new();
    public Balance Balance { get; set; } = new();
    public ulong Seed { get; set; }
    public ulong RngState { get; set; }
    public int Tick { get; set; }
    public double Influence { get; set; }
    public double DriftValue { get; set; }
    public DriftStage DriftStage { get; set; }
    public DriftBreakdown LastDrift { get; set; } = new();
    public double PendingDriftBump { get; set; }
    public List<Commune> Communes { get; set; } = new();
    public List<int[]> Neighbors { get; set; } = new();
    public List<int[]> Railways { get; set; } = new();
    public List<TrustEdge> Trust { get; set; } = new();
    public List<Project> Projects { get; set; } = new();
    public int NextProjectId { get; set; }
    public List<PendingSecession> PendingSecessions { get; set; } = new();
    public List<PendingEvent> PendingEvents { get; set; } = new();
    public List<string> FiredEvents { get; set; } = new();
    public bool WarCommittee { get; set; }
    public bool ForeignTrade { get; set; }
    public double DriftWarnedAt { get; set; }
    public int RecallPenaltyTicks { get; set; }
    public int LastCongressTick { get; set; }
    public Dictionary<string, int> IdeaNudges { get; set; } = new();
    public double TransportBonus { get; set; }
    public List<string> WidespreadTechs { get; set; } = new();
    public Scores Scores { get; set; } = new();
    public Outcome Outcome { get; set; }
    public int InitialCommuneCount { get; set; }
    public List<GameEvent> Log { get; set; } = new();
    public List<WeekRecord> History { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this, Json.Options);

    public static SaveGame FromJson(string json)
    {
        var save = JsonSerializer.Deserialize<SaveGame>(json, Json.Options)
                   ?? throw new InvalidDataException("Not a Politicom save file.");
        if (save.Version != CurrentVersion)
            throw new InvalidDataException($"Save file version {save.Version} is not supported (expected {CurrentVersion}).");
        return save;
    }
}
