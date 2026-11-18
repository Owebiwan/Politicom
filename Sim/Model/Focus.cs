namespace Politicom.Sim;

/// <summary>What a commune puts its effort into (as Civilization VII's town specialisations).</summary>
public enum CommuneFocus { None, Granary, Workshop, Fortress, Schoolhouse, Junction }

/// <summary>
/// Each commune's focus: one lever per town that needs no managing afterwards. The assembly is
/// asked to make itself the federation's granary, its workshop, a fort town on the line, a
/// schoolhouse for the region, or a rail junction for aid. Each focus gives more of one thing
/// for a little less of others; changing it takes a few months to settle.
/// </summary>
public static class Foci
{
    public static readonly CommuneFocus[] All = { CommuneFocus.None, CommuneFocus.Granary, CommuneFocus.Workshop, CommuneFocus.Fortress, CommuneFocus.Schoolhouse, CommuneFocus.Junction };

    /// <summary>Weeks before a commune will take up another focus.</summary>
    public const int SettleWeeks = 13;

    public static string Name(CommuneFocus f) => f switch
    {
        CommuneFocus.Granary => "Granary",
        CommuneFocus.Workshop => "Workshop",
        CommuneFocus.Fortress => "Fort town",
        CommuneFocus.Schoolhouse => "Schoolhouse",
        CommuneFocus.Junction => "Junction",
        _ => "No focus",
    };

    public static string Why(CommuneFocus f) => f switch
    {
        CommuneFocus.Granary => "The commune turns its land and hands to grain, oil and livestock for the federation.",
        CommuneFocus.Workshop => "Its workshops run longer shifts on tools, cloth and arms.",
        CommuneFocus.Fortress => "It makes itself a bastion: works, drill, stores of arms.",
        CommuneFocus.Schoolhouse => "Schools, reading rooms and the press: it teaches the region.",
        CommuneFocus.Junction => "Depots and carts at the crossroads: aid passes through it faster, to it and from it.",
        _ => "A bit of everything, as it always has.",
    };

    public static TechEffects Effects(CommuneFocus f) => f switch
    {
        CommuneFocus.Granary => new TechEffects { FoodProduction = 0.15, GoodsProduction = -0.04 },
        CommuneFocus.Workshop => new TechEffects { GoodsProduction = 0.15, FoodProduction = -0.04 },
        CommuneFocus.Fortress => new TechEffects { Defense = 0.1, FoodProduction = -0.05 }, // drill and works take hands from the fields, not arms from the workshops
        CommuneFocus.Schoolhouse => new TechEffects { Science = 0.3, Culture = 0.2, PoliticalEducation = 0.15, FoodProduction = -0.04, GoodsProduction = -0.04 },
        CommuneFocus.Junction => new TechEffects { GoodsProduction = -0.03 },
        _ => new TechEffects(),
    };

    /// <summary>How much more aid passes along a road when either end is a junction.</summary>
    public const double JunctionAid = 1.35;

    public static IEnumerable<string> Describe(CommuneFocus f) =>
        Effects(f).Describe().Select(d => d.Text.Replace(" (holders)", ""))
            .Concat(f == CommuneFocus.Junction ? new[] { $"Aid to and from it +{(JunctionAid - 1) * 100:0}%" } : Array.Empty<string>());
}
