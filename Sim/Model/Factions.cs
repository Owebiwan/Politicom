namespace Politicom.Sim;

public enum FactionKind { Centralists, Party, Planners, Localists }

/// <summary>
/// A faction: discontented communes banded together, drawn to one demand,
/// growing in strength while they're many and their grievance lasts, pressing it on the congress
/// when they are strong enough. Hierarchy drift with a face: while a faction presses, it adds drift.
/// </summary>
public sealed class Faction
{
    public FactionKind Kind { get; set; }
    /// <summary>0..1: at 1 they put their demand to the movement.</summary>
    public double Strength { get; set; }
    public List<int> Members { get; set; } = new();
    /// <summary>A figure (by id) who has taken up their cause, or null.</summary>
    public int? Leader { get; set; }
    /// <summary>How many times their demand has been granted.</summary>
    public int Conceded { get; set; }

    public static string Name(FactionKind k) => k switch
    {
        FactionKind.Centralists => "The centralists",
        FactionKind.Party => "The party's friends",
        FactionKind.Planners => "The planners",
        _ => "The localists",
    };

    public static string Demand(FactionKind k) => k switch
    {
        FactionKind.Centralists => "a permanent war committee over the militias",
        FactionKind.Party => "the party's commissars in every column",
        FactionKind.Planners => "a planning bureau run by engineers",
        _ => "autonomy: their communes to go their own way",
    };

    public static string Why(FactionKind k) => k switch
    {
        FactionKind.Centralists => "front communes under pressure, where few have time for the assemblies",
        FactionKind.Party => "communes where the party's cells have taken root",
        FactionKind.Planners => "communes where expertise has outrun political education",
        _ => "discontented communes with little trust in their neighbours",
    };
}
