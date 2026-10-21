using System.Text.Json.Serialization;

namespace Politicom.Sim;

public enum ProjectKind { FamineRelief, Railway, DefensiveCampaign, AteneoNetwork, Collectivization }

public sealed record ProjectDefinition(
    ProjectKind Kind,
    string Name,
    string Description,
    double Work,
    int ExpectedWeeks,
    CommuneValues Profile,
    int TargetsNeeded,
    bool FederationWide,
    double CompletionDrift);

public static class ProjectCatalog
{
    public static readonly IReadOnlyList<ProjectDefinition> All = new[]
    {
        new ProjectDefinition(ProjectKind.FamineRelief, "Famine relief",
            "Neighbours pool labor and grain to feed a hungry commune.",
            60, 6, new CommuneValues(1, 0, 0.3), 1, false, 0),
        new ProjectDefinition(ProjectKind.Railway, "Railway line",
            "Link two communes by rail: cheaper sharing, faster ideas — and organizers who keep the timetable.",
            400, 20, new CommuneValues(0.3, 0, 1), 2, false, 2),
        new ProjectDefinition(ProjectKind.DefensiveCampaign, "Defensive campaign",
            "Militias from behind the lines reinforce the front. Wears down the enemy if completed.",
            150, 8, new CommuneValues(0.2, 1, 0.4), 0, false, 0),
        new ProjectDefinition(ProjectKind.AteneoNetwork, "Ateneo network",
            "Workers' athenaeums with libraries, literacy classes and debate nights around a commune.",
            120, 10, new CommuneValues(0.6, -0.2, 0.5), 1, false, 0),
        new ProjectDefinition(ProjectKind.Collectivization, "Industrial collectivization",
            "Coordinate every industrial collective into one plan. Big output gains; planners gain power.",
            400, 20, new CommuneValues(1, 0, 0.6), 0, true, 3),
    };

    public static ProjectDefinition Get(ProjectKind kind) => All.First(p => p.Kind == kind);
}

/// <summary>A voluntary project. Communes opt in and out on their own; the player can only persuade.</summary>
public sealed class Project
{
    public int Id { get; init; }
    public ProjectKind Kind { get; init; }
    [JsonIgnore] public ProjectDefinition Definition => ProjectCatalog.Get(Kind);
    public int? Target { get; init; }
    public int? Target2 { get; init; }
    public int StartedTick { get; init; }
    public int Deadline { get; init; }
    public double Work { get; init; }
    [JsonInclude] public double Progress { get; internal set; }
    public HashSet<int> Invited { get; init; } = new();
    public HashSet<int> Participants { get; init; } = new();
    public Dictionary<int, double> Persuasion { get; init; } = new();
    /// <summary>Fraction of effort lost to coordination last tick.</summary>
    [JsonInclude] public double LastOverhead { get; internal set; }
    /// <summary>Effective effort last tick (used as militia support by defensive campaigns).</summary>
    [JsonInclude] public double LastOutput { get; internal set; }
    [JsonInclude] public ProjectStatus Status { get; internal set; } = ProjectStatus.Active;

    [JsonIgnore] public bool Active => Status == ProjectStatus.Active;
}

public enum ProjectStatus { Active, Completed, Failed }

public static class Coordination
{
    /// <summary>
    /// Fraction of effort lost to coordination. Grows superlinearly with participants, so small
    /// projects are easy and federation-spanning ones are hard. Political education, communication
    /// technology, drift stages and a war committee all reduce it.
    /// </summary>
    public static double Cost(int participants, double meanPolitical, double techReduction, DriftStage stage, bool warCommittee, Balance b)
    {
        if (participants <= 1) return 0;
        double friction = b.CoordinationScale * Math.Pow(participants - 1, b.CoordinationExponent);
        friction *= 1 - b.PoliticalCoordinationRelief * Math.Clamp(meanPolitical, 0, 1);
        friction *= 1 - Math.Clamp(techReduction, -0.5, 0.8);
        friction *= DriftEffects.Coordination(stage);
        if (warCommittee) friction *= 1 - b.WarCommitteeCoordination;
        return friction / (1 + friction);
    }
}
