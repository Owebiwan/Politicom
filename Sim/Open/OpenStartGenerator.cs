using System.Globalization;
using Politicom.Sim.Global;
using Politicom.Sim.Sandbox;

namespace Politicom.Sim.Open;

/// <summary>
/// The open world: the movement rises in any state of an era's world map. The state's largest
/// cities become communes, its government (holding the capital) is the enemy, and hostile
/// neighbours may back it. The result is an ordinary scenario, calibrated like a sandbox map so
/// any start is winnable but hard.
/// </summary>
public static class OpenStartGenerator
{
    public const string IdPrefix = "open";
    private const int MaxCommunes = 26, MinCommunes = 6, EndTick = 104;

    /// <summary>The world maps an open start can use, oldest first.</summary>
    public static readonly string[] Eras =
        { "-500", "1", "500", "800", "1000", "1200", "1400", "1500", "1650", "1783", "1815", "1871", "1918", "1936", "1960", "2000" };

    /// <summary>"500 BC", "AD 800", "1871".</summary>
    public static string EraName(string era) =>
        int.TryParse(era, out int y) ? y < 0 ? $"{-y} BC" : y < 1000 ? $"AD {y}" : $"{y}" : era;

    public static string Id(string era, string stateId, ulong seed) => $"{IdPrefix}:{era}:{stateId}:{seed}";

    /// <summary>Parses "open:ERA:STATE" or "open:ERA:STATE:SEED".</summary>
    public static bool TryParseId(string id, out string era, out string stateId, out ulong seed)
    {
        era = stateId = "";
        seed = 1;
        var parts = id.Split(':');
        if (parts.Length is < 3 or > 4 || parts[0] != IdPrefix) return false;
        era = parts[1];
        stateId = parts[2];
        return parts.Length == 3 || ulong.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out seed);
    }

    /// <summary>States you can start in: those with enough cities on their home territory.</summary>
    public static IEnumerable<AtlasState> Playable(Atlas atlas) =>
        atlas.States.Where(s => CommuneCities(s).Count >= MinCommunes).OrderBy(s => s.Name);

    public static ScenarioDefinition Generate(Atlas atlas, SandboxGenerator sandbox, string stateId, ulong seed, bool calibrate = true, int? length = null)
    {
        var state = atlas.State(stateId) ?? throw new ArgumentException($"No state '{stateId}' in the {atlas.Era} world.");
        var picked = CommuneCities(state);
        if (picked.Count < MinCommunes)
            throw new ArgumentException($"{state.Name} has too few cities on the map to play ({picked.Count}).");
        var rng = new Rng(seed ^ StableHash(atlas.Era + ":" + stateId) ^ 0x0BE2C0FFEEUL);
        int year = int.TryParse(atlas.Era, out var y) ? y : 1900;
        string country = StateText.Country(state.Name);
        var ideology = WorldSetup.Parse(state.Ideology);

        // ── Communes: the largest cities, minus the capital the government holds ──
        var capital = state.Capital ?? state.Cities[0];
        double eraEducation = Math.Clamp((year - 1750) / 250.0, 0, 1);
        var defs = new List<CommuneDefinition>();
        for (int i = 0; i < picked.Count; i++)
        {
            var c = picked[i];
            double rank = i / (double)picked.Count;
            var kind = rank < 0.2 ? CommuneKind.Industrial : rank < 0.5 ? CommuneKind.Mixed : CommuneKind.Agrarian;
            var (coll, mil, fed) = BaseValues(ideology);
            defs.Add(new CommuneDefinition
            {
                Name = c.Name,
                Kind = kind,
                Lon = c.Lon,
                Lat = c.Lat,
                Population = Math.Clamp(Math.Round(Math.Sqrt(Math.Max(c.Pop, 1)) / 60), 4, 60),
                Collectivism = Math.Round(Math.Clamp(coll + rng.Jitter(0.35), -1, 1), 2),
                Militancy = Math.Round(Math.Clamp(mil + rng.Jitter(0.35), -1, 1), 2),
                Federalism = Math.Round(Math.Clamp(fed + rng.Jitter(0.35), -1, 1), 2),
                Practical = Math.Round(Math.Clamp(0.05 + 0.15 * eraEducation + (kind == CommuneKind.Industrial ? 0.05 : 0) + rng.Jitter(0.04), 0.03, 0.6), 2),
                Political = Math.Round(Math.Clamp(0.1 + 0.12 * eraEducation + rng.Jitter(0.05), 0.03, 0.5), 2),
                Techs = kind == CommuneKind.Industrial ? new[] { year >= 1830 ? "collective_workshops" : year >= 1000 ? "craft_workshops" : "household_crafts" } : Array.Empty<string>(),
            });
        }

        // ── The enemy: the government, from the capital; the communes nearest it are the front ──
        string enemyName = EnemyName(year, ideology, state.Name, country);
        var (start, yearOffset) = ScenarioDefinition.Calendar(year, 1 + rng.Next(12), 1 + rng.Next(28));
        // An army sized to the rising it faces: a few small towns don't meet the force a whole country's cities do.
        double size = Math.Clamp(defs.Sum(d => d.Population) / 250.0, 0.3, 1.5);
        double strength = 3.0 * (0.7 + 0.12 * state.Power) * size;
        var enemy = new HostileState
        {
            Name = enemyName,
            Color = Lighten(state.Color),
            Strength = Math.Round(strength, 2),
            Growth = 0.06 * strength / 3,
            Sponsors = state.Neighbours.Where(n => atlas.State(n) is { } s && WorldSetup.BaseStance(WorldSetup.Parse(s.Ideology)) < -0.25).ToArray(),
        };
        int[] at = { 20 + rng.Next(15), 50 + rng.Next(20), 85 + rng.Next(10) };
        double[] force = { 25, 50, 85 };
        for (int o = 0; o < 3; o++)
            enemy.Offensives.Add(SandboxGenerator.Offensive(rng, enemyName, start, at[o], 6 + rng.Next(5), force[o] * strength / 3));
        int frontSize = Math.Clamp(defs.Count / 7, 2, 4);
        foreach (var d in defs.OrderBy(d => Km(new City(d.Name, d.Lon, d.Lat, 0), capital)).Take(frontSize))
            d.FrontOf = new[] { enemyName };

        var scenario = new ScenarioDefinition
        {
            Id = Id(atlas.Era, stateId, seed),
            Name = $"The {country} Commune",
            Description = $"{Ages.Format(start, yearOffset, "MMMM yyyy")}. {Rising(year, ideology, state.Name, country, defs[0].Name, capital.Name)} " +
                          $"Hold the federation together for two years. (Open start: {state.Name}, {EraName(atlas.Era)}.)",
            StartDate = start,
            YearOffset = yearOffset,
            EndTick = EndTick,
            NeighborCount = 3,
            NeighborRadiusKm = 30,
            World = atlas.Era,
            HomeState = stateId,
            Enemies = new List<HostileState> { enemy },
            Ally = new AllyState { Name = "", OfferInterval = 0 },
            Communes = defs,
        };
        scenario.Geography = Geography(state, defs);
        string? hero = year < 1789 ? $"the {(year < 500 ? "war leader" : "captain")} of {defs[Math.Min(defs.Count - 1, 1 + rng.Next(Math.Max(1, defs.Count - 1)))].Name}" : null;
        scenario.Events = sandbox.PickEvents(rng, year, country, defs[0].Name, defs, scenario.Enemies, hero);
        WorldSetup.Attach(scenario, atlas);
        if (length is int turns) scenario = scenario.WithLength(turns);
        return calibrate ? sandbox.Calibrate(scenario, seed ^ 0xA5A5UL, games: 20) : scenario; // real maps vary more than generated ones
    }

    /// <summary>The cities that become communes: the home cities, minus the capital the government holds.</summary>
    private static List<City> CommuneCities(AtlasState s)
    {
        var capital = s.Capital ?? s.Cities.FirstOrDefault();
        return capital is null ? new() : HomeCities(s).Where(c => Km(c, capital) > 20).Take(MaxCommunes).ToList();
    }

    /// <summary>The state's cities on its home territory (the piece around its capital), large ones first, without near-duplicates.</summary>
    internal static List<City> HomeCities(AtlasState s)
    {
        var capital = s.Capital ?? s.Cities.FirstOrDefault();
        if (capital is null) return new List<City>();
        var home = s.Rings.FirstOrDefault(r => Inside(r, capital.Lon, capital.Lat)) ?? s.Rings.FirstOrDefault();
        if (home is null) return new List<City>();
        var picked = new List<City>();
        foreach (var c in s.Cities.OrderByDescending(c => c.Pop))
        {
            bool onHome = Inside(home, c.Lon, c.Lat) || home.Any(p => Math.Abs(p[0] - c.Lon) + Math.Abs(p[1] - c.Lat) < 0.35);
            if (onHome && picked.All(p => Km(p, c) > 15 && p.Name != c.Name)) picked.Add(c);
        }
        return picked;
    }

    /// <summary>The home outline, clipped to the area around the communes, as the region's land.</summary>
    private static GeographyDefinition Geography(AtlasState s, List<CommuneDefinition> defs)
    {
        double pad = 1.2;
        double x0 = defs.Min(d => d.Lon) - pad, x1 = defs.Max(d => d.Lon) + pad, y0 = defs.Min(d => d.Lat) - pad, y1 = defs.Max(d => d.Lat) + pad;
        var capital = s.Capital ?? s.Cities[0];
        var home = s.Rings.FirstOrDefault(r => Inside(r, capital.Lon, capital.Lat)) ?? s.Rings[0];
        var land = ClipToBox(home.Select(p => (p[0], p[1])).ToList(), x0, y0, x1, y1);
        return new GeographyDefinition
        {
            Bbox = new[] { x0, y0, x1, y1 },
            Land = land.Count >= 3 ? new List<double[][]> { land.Select(p => new[] { Math.Round(p.Item1, 4), Math.Round(p.Item2, 4) }).ToArray() } : new(),
            DrawProvinces = false,
        };
    }

    /// <summary>FNV-1a: the same in every process (string.GetHashCode is randomized per run).</summary>
    internal static ulong StableHash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return h;
    }

    /// <summary>"the Roman Empire", "France": a state's name as it reads inside a sentence.</summary>
    internal static string Proper(string name)
    {
        string[] titles = { "Empire", "Caliphate", "Sultanate", "Khanate", "Khaganate", "Kingdom", "Emirate", "Emirates", "states", "kingdoms",
                            "Dynasty", "Union", "Confederation", "Republic", "Order", "Horde", "Khanates", "city-states", "peoples", "Islands" };
        string[] lead = { "Kingdom", "Principality", "Duchy", "Grand", "Sultanate", "Emirate", "Republic", "Empire", "Caliphate", "Union" };
        var words = name.Split(' ');
        bool titled = words.Length > 1 && (titles.Contains(words[^1]) || lead.Contains(words[0]));
        return titled && !name.StartsWith("The ") ? "the " + name : name;
    }

    /// <summary>The government's forces, named for the age: legions and royal hosts before there were "government forces".</summary>
    internal static string EnemyName(int year, Ideology ideology, string stateName, string country)
    {
        if (year >= 1789 && ideology is not (Ideology.Tribal or Ideology.Theocracy or Ideology.Empire)) return $"{country} government forces";
        string of = Proper(stateName);
        return ideology switch
        {
            Ideology.Tribal => $"war bands of {of}",
            Ideology.Theocracy => $"holy army of {of}",
            Ideology.Empire => year >= 1789 ? $"imperial forces of {of}" : $"imperial army of {of}",
            _ => year < 1450 ? $"royal host of {of}" : $"royal army of {of}",
        };
    }

    /// <summary>How the rising is described, by age.</summary>
    internal static string Rising(int year, Ideology ideology, string stateName, string country, string city, string capital)
    {
        string of = Proper(stateName);
        string rulers = ideology switch
        {
            Ideology.Tribal => $"the war chiefs at {capital} are calling their warriors together",
            Ideology.Theocracy => $"the priests in {capital} are calling the faithful to arms",
            _ when year < 500 => $"the rulers in {capital} are mustering their army",
            _ when year < 1450 => $"the lords in {capital} are gathering their knights",
            _ when year < 1789 => $"the court in {capital} is raising an army",
            _ => $"the government in {capital} is gathering its forces",
        };
        string who = year switch
        {
            < 500 => $"Slaves, peasants and craftsmen have risen across {of}: they hold {city} and the towns around it",
            < 1450 => $"A rising of the commons has spread across {of}: peasants and townsfolk hold {city} and the towns around it",
            < 1789 => $"The commons of {of} have risen against the crown: they hold {city} and the towns around it",
            _ => $"Strikes and risings have spread across {country}: workers and peasants hold {city} and the towns around it",
        };
        return $"{who}, and {rulers}.";
    }

    private static (double Coll, double Mil, double Fed) BaseValues(Ideology i) => i switch
    {
        Ideology.Fascist => (0.5, 0.7, 0.3),
        Ideology.Monarchy => (0.4, 0.5, 0.2),
        Ideology.Conservative => (0.4, 0.4, 0.3),
        Ideology.Empire => (0.4, 0.6, 0.2),
        Ideology.Tribal => (0.7, 0.4, 0.6),
        Ideology.Theocracy => (0.5, 0.4, 0.2),
        Ideology.Communist => (0.6, 0.4, 0.4),
        _ => (0.35, 0.25, 0.4),
    };

    private static string Lighten(string color)
    {
        try
        {
            int r = Convert.ToInt32(color.Substring(1, 2), 16), g = Convert.ToInt32(color.Substring(3, 2), 16), b = Convert.ToInt32(color.Substring(5, 2), 16);
            int L(int v) => Math.Min(255, v + 60);
            return $"#{L(r):x2}{L(g):x2}{L(b):x2}";
        }
        catch { return "#eb4d40"; }
    }

    private static double Km(City a, City b) => WorldSetup.DistanceKm(a.Lon, a.Lat, b.Lon, b.Lat);

    private static bool Inside(double[][] ring, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            if ((ring[i][1] > y) != (ring[j][1] > y) &&
                x < (ring[j][0] - ring[i][0]) * (y - ring[i][1]) / (ring[j][1] - ring[i][1]) + ring[i][0])
                inside = !inside;
        return inside;
    }

    private static List<(double, double)> ClipToBox(List<(double X, double Y)> poly, double x0, double y0, double x1, double y1)
    {
        List<(double X, double Y)> Clip(List<(double X, double Y)> pts, Func<(double X, double Y), double> side)
        {
            var result = new List<(double X, double Y)>();
            for (int i = 0; i < pts.Count; i++)
            {
                var cur = pts[i];
                var next = pts[(i + 1) % pts.Count];
                double sc = side(cur), sn = side(next);
                if (sc <= 0) result.Add(cur);
                if ((sc < 0 && sn > 0) || (sc > 0 && sn < 0))
                {
                    double t = sc / (sc - sn);
                    result.Add((cur.X + (next.X - cur.X) * t, cur.Y + (next.Y - cur.Y) * t));
                }
            }
            return result;
        }
        poly = Clip(poly, p => x0 - p.X);
        poly = Clip(poly, p => p.X - x1);
        poly = Clip(poly, p => y0 - p.Y);
        poly = Clip(poly, p => p.Y - y1);
        double Snap(double v, double lo, double hi) => Math.Abs(v - lo) < 1e-9 ? lo : Math.Abs(v - hi) < 1e-9 ? hi : v;
        return poly.Select(p => (Snap(p.X, x0, x1), Snap(p.Y, y0, y1))).ToList();
    }
}
