namespace Politicom.Sim;

public enum StrugglePhase { Cooperation, Tension, Hostility }

public sealed record StruggleCatalyst(int Tick, string Why, double Amount);

/// <summary>
/// The struggle between allies, as Crusader Kings' struggles track a contest that no one side can
/// win outright: the movement against its allies of convenience (the Republic and the party in
/// Catalonia, the Bolsheviks in Ukraine). Tension (0..100) moves it through its phases:
/// cooperation, tension, hostility. At the top it breaks into open conflict; kept low for half a
/// year, it ends in recognition. Catalysts move it: arms taken or refused, cells exposed or
/// tolerated, the party's growth, a war going badly.
/// </summary>
public sealed class Struggle
{
    public string Name { get; set; } = "";
    /// <summary>Who the movement struggles with (the ally's or the rival's name).</summary>
    public string With { get; set; } = "";
    public double Tension { get; set; } = 25;
    /// <summary>Turns tension has stayed low, toward recognition.</summary>
    public int CalmTicks { get; set; }
    public int LastRupture { get; set; } = -999;
    public bool Recognized { get; set; }
    /// <summary>Last turn's drift of tension, part by part, for the dashboard.</summary>
    public List<Part> LastChange { get; set; } = new();
    /// <summary>The last few choices that moved it, newest last.</summary>
    public List<StruggleCatalyst> Catalysts { get; set; } = new();

    /// <summary>The phase: entered at 35 and 65, left only 5 below, so it doesn't flicker at the edge.</summary>
    public StrugglePhase Phase { get; set; } = StrugglePhase.Cooperation;

    /// <summary>Moves the phase with tension; true when it changed.</summary>
    public bool UpdatePhase()
    {
        var next = Phase switch
        {
            StrugglePhase.Cooperation => Tension >= 65 ? StrugglePhase.Hostility : Tension >= 35 ? StrugglePhase.Tension : StrugglePhase.Cooperation,
            StrugglePhase.Tension => Tension >= 65 ? StrugglePhase.Hostility : Tension < 30 ? StrugglePhase.Cooperation : StrugglePhase.Tension,
            _ => Tension < 30 ? StrugglePhase.Cooperation : Tension < 60 ? StrugglePhase.Tension : StrugglePhase.Hostility,
        };
        bool changed = next != Phase;
        Phase = next;
        return changed;
    }

    public static string PhaseName(StrugglePhase p) => p switch
    {
        StrugglePhase.Cooperation => "Cooperation",
        StrugglePhase.Tension => "Tension",
        _ => "Hostility",
    };

    public static string PhaseEffect(StrugglePhase p) => p switch
    {
        StrugglePhase.Cooperation => "a united front: +5% defence",
        StrugglePhase.Tension => "no effect yet",
        _ => "the allies hold back: −5% defence, no arms offered, party cells grow faster, imports cut",
    };
}
