using System.Text.Json.Serialization;

namespace Politicom.Sim;

public enum DriftStage { Free, PermanentCouncils, Commissars, Police, State }

/// <summary>
/// The signature mechanic: informal power accumulating until a quasi-state crystallizes.
/// Each stage gives real bonuses and removes a piece of self-management. Stages are sticky —
/// drift has to fall well below a threshold (hysteresis) before a stage is undone, and
/// <see cref="DriftStage.State"/> can never be undone.
/// </summary>
public sealed class DriftTracker
{
    public static readonly double[] Thresholds = { 25, 50, 75, 100 };

    public double Value { get; private set; }
    public DriftStage Stage { get; private set; }

    /// <summary>Returns the new stage if it changed.</summary>
    public DriftStage? Apply(double delta, double hysteresis)
    {
        if (Stage == DriftStage.State) return null;
        Value = Math.Clamp(Value + delta, 0, 100);
        var old = Stage;
        while (Stage < DriftStage.State && Value >= Thresholds[(int)Stage]) Stage++;
        while (Stage is > DriftStage.Free and < DriftStage.State && Value < Thresholds[(int)Stage - 1] - hysteresis) Stage--;
        return Stage != old ? Stage : null;
    }

    internal void Restore(double value, DriftStage stage)
    {
        Value = value;
        Stage = stage;
    }

    /// <summary>Drift needed to enter the next stage, or null at State.</summary>
    public double? NextThreshold => Stage == DriftStage.State ? null : Thresholds[(int)Stage];
}

/// <summary>Where this tick's drift came from, so the player can see what is pushing it.</summary>
public sealed class DriftBreakdown
{
    public double Baseline { get; set; }
    public double Technology { get; set; }
    public double ExpertTrap { get; set; }
    public double WarCommittee { get; set; }
    public double WarStress { get; set; }
    public double Militarization { get; set; }
    public double LargeProjects { get; set; }
    public double Events { get; set; }
    public double PoliticalEducationBrake { get; set; }
    public double TechnologyBrake { get; set; }

    [JsonIgnore] public double Pressure => Baseline + Technology + ExpertTrap + WarCommittee + WarStress + Militarization + LargeProjects;
    [JsonIgnore] public double Brakes => PoliticalEducationBrake + TechnologyBrake;
    [JsonIgnore] public double Net => Pressure - Brakes + Events;

    public IEnumerable<(string Label, double Value)> Items()
    {
        yield return ("Organizers accumulating power", Baseline);
        yield return ("Double-edged technologies", Technology);
        yield return ("Expert trap (practical > political)", ExpertTrap);
        yield return ("War committee", WarCommittee);
        yield return ("Wartime emergency", WarStress);
        yield return ("Government militarization", Militarization);
        yield return ("Large federal projects", LargeProjects);
        yield return ("Events this week", Events);
        yield return ("Political education", -PoliticalEducationBrake);
        yield return ("Mandates, recall, rotation", -TechnologyBrake);
    }
}

/// <summary>What each stage gives and takes away.</summary>
public static class DriftEffects
{
    private static readonly double[] ProductivityByStage = { 1.0, 1.04, 1.08, 1.10, 1.10 };
    private static readonly double[] DefenseByStage = { 0, 0.05, 0.2, 0.3, 0.3 };
    private static readonly double[] CoordinationByStage = { 1.0, 0.85, 0.7, 0.6, 0.6 };
    private static readonly double[] TrustGrowthByStage = { 1.0, 0.9, 0.75, 0.6, 0.6 };
    private static readonly double[] FreeRidingByStage = { 1.0, 0.9, 0.6, 0.3, 0.3 };
    private static readonly double[] PoliticalEducationByStage = { 1.0, 0.9, 0.7, 0.5, 0.5 };
    private static readonly double[] RecallCostByStage = { 1.0, 1.5, 2.0, 3.0, 3.0 };
    private static readonly double[] TrustErosionByStage = { 0, 0, 0.0005, 0.0015, 0.0015 };
    private static readonly double[] SecessionPressureByStage = { 0, 0.05, 0.1, 0.2, 0.2 };

    public static double Productivity(DriftStage s) => ProductivityByStage[(int)s];
    public static double Defense(DriftStage s) => DefenseByStage[(int)s];
    public static double Coordination(DriftStage s) => CoordinationByStage[(int)s];
    public static double TrustGrowth(DriftStage s) => TrustGrowthByStage[(int)s];
    public static double FreeRiding(DriftStage s) => FreeRidingByStage[(int)s];
    public static double PoliticalEducation(DriftStage s) => PoliticalEducationByStage[(int)s];
    public static double RecallCost(DriftStage s) => RecallCostByStage[(int)s];
    public static double TrustErosion(DriftStage s) => TrustErosionByStage[(int)s];
    public static double SecessionPressure(DriftStage s) => SecessionPressureByStage[(int)s];

    public static string Name(DriftStage s) => s switch
    {
        DriftStage.Free => "Free association",
        DriftStage.PermanentCouncils => "Permanent councils",
        DriftStage.Commissars => "Commissars",
        DriftStage.Police => "Police force",
        DriftStage.State => "A state",
        _ => s.ToString(),
    };

    public static string Describe(DriftStage s) => s switch
    {
        DriftStage.Free => "Delegates are mandated and recallable. Nothing stands above the assemblies.",
        DriftStage.PermanentCouncils => "Standing councils make decisions faster (+4% output, cheaper coordination) but assemblies are consulted less.",
        DriftStage.Commissars => "Commissars enforce federal decisions (+20% defense) at the cost of trust and political education.",
        DriftStage.Police => "An internal police force ends free-riding by coercion (+30% defense). Trust erodes everywhere.",
        DriftStage.State => "The revolution is over. You won the war, perhaps, and lost the revolution.",
        _ => "",
    };
}
