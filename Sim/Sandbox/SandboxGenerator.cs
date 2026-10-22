using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Politicom.Sim.Sandbox;

/// <summary>
/// Generates an alternate-history region from a seed: communes, values, enemies, offensives and
/// events. The result is an ordinary <see cref="ScenarioDefinition"/>, so the simulation, AI,
/// batch runs, Godot and save files all treat it like a hand-made scenario.
/// </summary>
public sealed class SandboxGenerator
{
    public const string IdPrefix = "sandbox";
    public const int DefaultSize = 26;
    public static readonly int[] Sizes = { 20, 26, 32, 40 };

    private const int EventsPerMap = 5;
    private const int EndTick = 104; // two years of weekly turns

    // Enemy strength, tuned with `politicom batch --scenario sandbox`.
    private const double BaseStrength = 3.0;
    private const double Growth = 0.06;
    private static readonly double[] OffensiveStrength = { 25, 50, 85 };
    private const double TwoEnemyShare = 0.65;

    private static readonly string[] Starts = { "Bel", "Kor", "Vel", "San", "Mar", "Dor", "Ost", "Tre", "Gal", "Ran", "Lis", "Mon", "Var", "Zab", "Cer", "Pol", "Kas", "Ner", "Tor", "Al", "Bre", "Sol", "Vit", "Hal" };
    private static readonly string[] Middles = { "a", "o", "e", "i", "u", "", "", "an", "or" };
    private static readonly string[] Ends = { "vik", "ana", "ova", "grad", "ino", "era", "ets", "ia", "ard", "enza", "ow", "ice", "orf", "ak", "on", "esti", "avo", "ul" };
    private static readonly string[] FirstNames = { "Nestor", "Lucía", "Anselmo", "Maria", "Ivan", "Teresa", "Gustav", "Olga", "Mateo", "Rosa", "Pavel", "Elena", "Louise", "Errico", "Federica", "Buenaventura" };
    private static readonly string[] EnemyNames = { "Royal Army", "National Army", "White Army", "Imperial Army", "Federal Army", "Volunteer Army" };
    private static readonly string[] TurncoatNames = { "Red Army", "Republican Army", "People's Guard" };
    private static readonly string[] EnemyColors = { "#eb4d40", "#5b8def", "#dfe6f0", "#e0a030", "#9b6bd8" };
    private static readonly string[] Compass = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };
    private static readonly string[] Flavours =
    {
        "The old order in {region} has collapsed, and the villages and workshops are governing themselves.",
        "A general strike in {city} has become a revolution; the land belongs to those who work it.",
        "The soldiers came home to {region} and did not give their rifles back.",
        "When the landlords fled {region}, the peasants divided the estates and the workers took the factories.",
    };

    // Calibration: the balanced AI should survive about this often on every generated map.
    private const double TargetSurvival = 0.7;
    private const int CalibrationGames = 10;
    private const int CalibrationSteps = 6;

    private readonly List<(string Window, string Json)> _templates;
    private readonly TechCatalog _techs;

    private SandboxGenerator(List<(string, string)> templates, TechCatalog techs)
    {
        _templates = templates;
        _techs = techs;
    }

    /// <summary>The tech catalog is needed to play the calibration games.</summary>
    public static SandboxGenerator Parse(string templatesJson, TechCatalog techs)
    {
        using var doc = JsonDocument.Parse(templatesJson, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var templates = doc.RootElement.GetProperty("templates").EnumerateArray()
            .Select(t => (t.GetProperty("window").GetString() ?? "mid", t.GetProperty("event").GetRawText()))
            .ToList();
        if (templates.Count < EventsPerMap)
            throw new InvalidDataException($"Sandbox needs at least {EventsPerMap} event templates, found {templates.Count}.");
        return new SandboxGenerator(templates, techs);
    }

    public static SandboxGenerator Load(string dataDir) =>
        Parse(File.ReadAllText(DataPaths.SandboxTemplates(dataDir)), TechCatalog.Load(DataPaths.Technologies(dataDir)));

    public static string Id(ulong seed, int communes = DefaultSize) => $"{IdPrefix}:{seed}:{communes}";

    /// <summary>Parses "sandbox:SEED" or "sandbox:SEED:COMMUNES".</summary>
    public static bool TryParseId(string id, out ulong seed, out int communes)
    {
        seed = 0;
        communes = DefaultSize;
        var parts = id.Split(':');
        if (parts.Length is < 2 or > 3 || parts[0] != IdPrefix) return false;
        if (!ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out seed)) return false;
        return parts.Length == 2 || int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out communes);
    }

    /// <param name="calibrate">Tune enemy strength so the map is winnable but hard (default).</param>
    /// <param name="length">Turns to play (null: the default two years). Calibration uses the full length.</param>
    public ScenarioDefinition Generate(ulong seed, int communes = DefaultSize, bool calibrate = true, int? length = null)
    {
        if (communes is < 12 or > 60) throw new ArgumentOutOfRangeException(nameof(communes), "A sandbox region needs 12–60 communes.");
        var rng = new Rng(seed ^ 0x5A4DB0C5E1F2A3B7UL);
        var used = new HashSet<string>();
        string region = Name(rng, used);

        // ── Geography ─────────────────────────────────────────────────────────
        double scale = Math.Sqrt(communes / (double)DefaultSize);
        double width = 250 * scale, height = 220 * scale; // km
        double spacing = Math.Sqrt(width * height / communes);
        var points = SpacedPoints(rng, communes, width, height, 0.6 * spacing);

        int cityCount = communes >= DefaultSize ? 2 : 1;
        var cities = PickCities(rng, points, cityCount, width);

        // Ideological centres; each commune blends them by distance.
        var centres = Enumerable.Range(0, 3).Select(_ => (
            Point: (X: rng.NextDouble() * width, Y: rng.NextDouble() * height),
            Values: new CommuneValues(-0.2 + 1.2 * rng.NextDouble(), -0.5 + 1.4 * rng.NextDouble(), -0.5 + 1.3 * rng.NextDouble()))).ToList();

        var defs = new List<CommuneDefinition>();
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            double toCity = cities.Min(c => Dist(p, points[c]));
            var kind = cities.Contains(i) ? CommuneKind.Industrial
                : toCity < 1.5 * spacing || rng.Chance(0.12) ? CommuneKind.Mixed
                : CommuneKind.Agrarian;
            double population = kind switch
            {
                CommuneKind.Industrial => 30 + 30 * rng.NextDouble(),
                CommuneKind.Mixed => 10 + 10 * rng.NextDouble(),
                _ => 4 + 8 * rng.NextDouble(),
            };

            double wSum = 0, coll = 0, mil = 0, fed = 0;
            foreach (var (point, values) in centres)
            {
                double weight = 1 / Math.Pow(Dist(p, point) + spacing, 2);
                wSum += weight;
                coll += weight * values.Collectivism;
                mil += weight * values.Militancy;
                fed += weight * values.Federalism;
            }
            double Axis(double v) => Math.Round(Math.Clamp(v / wSum + rng.Jitter(0.25), -1, 1), 2);
            var c = new CommuneDefinition
            {
                Name = Name(rng, used),
                Kind = kind,
                Lon = Math.Round(10 + p.X / 77.3, 3),
                Lat = Math.Round(45 + p.Y / 110.6, 3),
                Population = Math.Round(population),
                Collectivism = Axis(coll),
                Militancy = Axis(mil),
                Federalism = Axis(fed),
            };
            c.Practical = Math.Round(kind switch
            {
                CommuneKind.Industrial => 0.25 + 0.15 * rng.NextDouble(),
                CommuneKind.Mixed => 0.15 + 0.1 * rng.NextDouble(),
                _ => 0.06 + 0.09 * rng.NextDouble(),
            }, 2);
            c.Political = Math.Round(Math.Clamp(0.08 + 0.15 * CommuneValues.To01(c.Collectivism) + (kind == CommuneKind.Industrial ? 0.08 : 0) + rng.Jitter(0.05), 0.02, 0.5), 2);
            defs.Add(c);
        }

        // ── Enemies ───────────────────────────────────────────────────────────
        int enemyCount = communes >= 30 || rng.Chance(0.5) ? 2 : 1;
        bool turncoat = enemyCount == 2 && rng.Chance(0.5);
        var start = new DateOnly(1870 + rng.Next(69), 1 + rng.Next(12), 1 + rng.Next(28));
        var colors = EnemyColors.OrderBy(_ => rng.NextDouble()).ToList();
        var centre = (X: width / 2, Y: height / 2);
        double angle1 = rng.NextDouble() * 2 * Math.PI;
        double angle2 = angle1 + Math.PI * (0.6 + 0.8 * rng.NextDouble());
        int frontSize = Math.Clamp(communes / 8, 2, 4);
        var enemies = new List<HostileState>();
        var directions = new List<string>();
        var taken = new HashSet<int>();

        for (int k = 0; k < enemyCount; k++)
        {
            bool isTurncoat = turncoat && k == 1;
            double angle = k == 0 ? angle1 : angle2;
            string name = isTurncoat ? Pick(rng, TurncoatNames) : Pick(rng, EnemyNames.Where(n => enemies.All(e => e.Name != n)).ToArray());
            var dir = (X: Math.Cos(angle), Y: Math.Sin(angle));
            var front = Enumerable.Range(0, points.Count).Where(i => !taken.Contains(i))
                .OrderByDescending(i => (points[i].X - centre.X) * dir.X + (points[i].Y - centre.Y) * dir.Y)
                .Take(frontSize).ToList();
            foreach (var i in front)
            {
                defs[i].FrontOf = defs[i].FrontOf.Append(name).ToArray();
                taken.Add(i);
            }
            directions.Add(Compass[(int)Math.Round(((angle % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI) / (Math.PI / 4)) % 8]);

            double share = enemyCount == 2 ? TwoEnemyShare : 1;
            var e = new HostileState { Name = name, Color = colors[k] };
            if (isTurncoat)
            {
                // An ally of convenience until mid-war, like the Reds in the Free Territory.
                e.Offensives.Add(Offensive(rng, name, start, 35 + rng.Next(15), 8, OffensiveStrength[1] * share));
                e.Offensives.Add(Offensive(rng, name, start, 80 + rng.Next(10), 14, OffensiveStrength[2] * share));
            }
            else
            {
                e.Strength = Math.Round(BaseStrength * share * (0.8 + 0.4 * rng.NextDouble()), 2);
                e.Growth = Growth * share;
                int offensives = k == 0 ? 2 + rng.Next(2) : 1 + rng.Next(2);
                int[] at = { 20 + rng.Next(15), 50 + rng.Next(20), 85 + rng.Next(10) };
                for (int o = 0; o < offensives; o++)
                {
                    int slot = offensives == 3 ? o : k == 0 ? o + 1 : 2 - o;
                    e.Offensives.Add(Offensive(rng, name, start, at[slot], 6 + rng.Next(5), OffensiveStrength[slot] * share));
                }
                e.Offensives.Sort((a, b) => a.Tick.CompareTo(b.Tick));
            }
            enemies.Add(e);
        }

        // ── Starting techs ────────────────────────────────────────────────────
        foreach (var c in cities) AddTech(defs[c], "collective_workshops");
        if (rng.Chance(0.5)) AddTech(defs[cities[0]], "libertarian_press");
        if (rng.Chance(0.5)) AddTech(defs[taken.First()], "militia_doctrine");
        if (rng.Chance(0.4)) AddTech(defs[rng.Next(defs.Count)], "ateneo_libraries");

        // ── Ally, story and events ────────────────────────────────────────────
        var ally = rng.Chance(0.5)
            ? new AllyState { Name = "Provisional Government", NextOfferTick = 8 + rng.Next(10), OfferInterval = 20 + rng.Next(10), ArmsGoods = 25 }
            : new AllyState { Name = "", OfferInterval = 0 };

        string city = defs[cities.OrderByDescending(c => defs[c].Population).First()].Name;
        string flavour = Pick(rng, Flavours).Replace("{region}", region).Replace("{city}", city);
        string threat = enemyCount == 1
            ? $"The {enemies[0].Name} is coming from the {directions[0]}."
            : $"The {enemies[0].Name} is coming from the {directions[0]}, and the {enemies[1].Name} from the {directions[1]}" + (turncoat ? " — for now an ally." : ".");
        string title = Pick(rng, new[] { $"The Free Communes of {region}", $"Revolutionary {region}", $"The {region} Federation" });

        var scenario = new ScenarioDefinition
        {
            Id = Id(seed, communes),
            Name = title,
            Description = $"{start:MMMM yyyy}. {flavour} {threat} Hold {region} together as a free federation for two years. (Sandbox map, seed {seed}.)",
            StartDate = start,
            EndTick = EndTick,
            NeighborCount = 3,
            NeighborRadiusKm = Math.Round(0.9 * spacing, 1),
            Enemies = enemies,
            Ally = ally,
            Communes = defs,
        };
        scenario.Events = PickEvents(rng, region, city, defs, enemies);
        scenario.Railways = Railways(rng, defs, points, spacing);
        var angles = enemyCount == 2 ? new[] { angle1, angle2 } : new[] { angle1 };
        scenario.Geography = GeographyGenerator.Generate(seed, points, defs, spacing, angles,
            cities.OrderByDescending(c => defs[c].Population).First());
        Neighbours(rng, used, scenario, enemies, angles);
        if (length is int turns) scenario = scenario.WithLength(turns);
        return calibrate ? Calibrate(scenario, seed) : scenario;
    }

    /// <summary>
    /// A railway network: the cities and towns joined by the shortest connections (a minimum
    /// spanning tree), plus branch lines out to some of the villages near a station.
    /// </summary>
    private static readonly string[] NeighbourColors = { "#6e7f8f", "#8a7a5e", "#7a8b6a", "#857089", "#6f8a7e", "#8f6e6e" };
    private static readonly (string Ideology, string Form)[] Regimes =
    {
        ("liberal", "Republic of"), ("conservative", "Republic of"), ("fascist", "National State of"),
        ("monarchy", "Kingdom of"), ("communist", "People's Republic of"), ("liberal", "Confederation of"),
    };

    /// <summary>
    /// Names, regimes and power for the neighbouring countries, and the outside states the
    /// simulation plays. A neighbour lying in an enemy's direction may back that enemy.
    /// </summary>
    private static void Neighbours(Rng rng, HashSet<string> used, ScenarioDefinition scenario, List<HostileState> enemies, double[] angles)
    {
        var geo = scenario.Geography!;
        for (int i = 0; i < geo.Neighbours.Count; i++)
        {
            var n = geo.Neighbours[i];
            var (ideology, form) = Pick(rng, Regimes);
            n.Name = $"{form} {Name(rng, used)}";
            n.Ideology = ideology;
            n.Power = 1 + rng.Next(5);
            n.Color = NeighbourColors[i % NeighbourColors.Length];
            var parsed = Global.WorldSetup.Parse(ideology);
            scenario.States.Add(new Global.StateDefinition
            {
                Id = n.Id,
                Name = n.Name,
                Ideology = parsed,
                Power = n.Power,
                Proximity = 1,
                InitialStance = Math.Round(Global.WorldSetup.BaseStance(parsed) + rng.Jitter(0.2), 2),
                Neighbours = geo.Neighbours.Where(x => x != n).Select(x => x.Id).ToArray(),
            });
            for (int k = 0; k < enemies.Count && k < angles.Length; k++)
            {
                double diff = Math.Abs(Math.IEEERemainder(n.Angle - angles[k], 2 * Math.PI));
                if (diff < Math.PI / 3) enemies[k].Sponsors = enemies[k].Sponsors.Append(n.Id).ToArray();
            }
        }
    }

    private static List<string[]> Railways(Rng rng, List<CommuneDefinition> defs, List<(double X, double Y)> points, double spacing)
    {
        var stations = Enumerable.Range(0, defs.Count).Where(i => defs[i].Kind != CommuneKind.Agrarian).ToList();
        var lines = new List<string[]>();
        if (stations.Count == 0) return lines;
        var connected = new List<int> { stations[0] };
        var remaining = stations.Skip(1).ToList();
        while (remaining.Count > 0)
        {
            var (from, to) = (from a in connected from b in remaining orderby Dist(points[a], points[b]) select (a, b)).First();
            lines.Add(new[] { defs[from].Name, defs[to].Name });
            connected.Add(to);
            remaining.Remove(to);
        }
        foreach (int v in Enumerable.Range(0, defs.Count).Where(i => defs[i].Kind == CommuneKind.Agrarian))
        {
            int nearest = connected.OrderBy(s => Dist(points[v], points[s])).First();
            if (Dist(points[v], points[nearest]) < 1.5 * spacing && rng.Chance(0.35))
                lines.Add(new[] { defs[nearest].Name, defs[v].Name });
        }
        return lines;
    }

    /// <summary>
    /// Maps differ wildly in how defensible they are (who is on the front, where the cities sit,
    /// one enemy or two). Bisect a strength multiplier for all enemies until the built-in balanced
    /// player survives about <see cref="TargetSurvival"/> of a few quick games: every map is
    /// winnable, none is a walkover. Deterministic: the trial games are seeded from the map seed.
    /// </summary>
    internal ScenarioDefinition Calibrate(ScenarioDefinition scenario, ulong seed)
    {
        double lo = Math.Log(0.25), hi = Math.Log(4);
        for (int step = 0; step < CalibrationSteps; step++)
        {
            double mid = (lo + hi) / 2;
            var trial = Scaled(scenario, Math.Exp(mid));
            int survived = 0;
            for (int g = 0; g < CalibrationGames; g++)
            {
                var w = World.Create(trial, _techs, seed: seed * 7919 + (ulong)g);
                var policy = new Ai.Balanced();
                while (w.Outcome == Outcome.Ongoing)
                {
                    policy.Act(w);
                    w.Step();
                }
                if (w.Outcome == Outcome.Survived) survived++;
            }
            if (survived / (double)CalibrationGames >= TargetSurvival) lo = mid; // too easy: stronger enemies
            else hi = mid;
        }
        return Scaled(scenario, Math.Exp((lo + hi) / 2));
    }

    private static ScenarioDefinition Scaled(ScenarioDefinition scenario, double multiplier)
    {
        var copy = JsonSerializer.Deserialize<ScenarioDefinition>(JsonSerializer.Serialize(scenario, Json.Options), Json.Options)!;
        foreach (var e in copy.Enemies)
        {
            e.Strength = Math.Round(e.Strength * multiplier, 2);
            e.Growth = Math.Round(e.Growth * multiplier, 4);
            foreach (var o in e.Offensives) o.Strength = Math.Round(o.Strength * multiplier, 1);
        }
        return copy;
    }

    internal List<EventDefinition> PickEvents(Rng rng, string region, string city, List<CommuneDefinition> defs, List<HostileState> enemies)
    {
        var villages = defs.Where(c => c.Kind == CommuneKind.Agrarian && c.FrontOf.Length == 0).Select(c => c.Name).ToList();
        if (villages.Count == 0) villages = defs.Where(c => c.FrontOf.Length == 0).Select(c => c.Name).ToList();
        string hero = $"{Pick(rng, FirstNames)} {Name(rng, new HashSet<string>())}";
        var chosen = _templates.OrderBy(_ => rng.NextDouble()).Take(EventsPerMap).ToList();
        var events = new List<EventDefinition>();
        var ticks = new HashSet<int>();
        foreach (var (window, json) in chosen)
        {
            int tick = window switch
            {
                "early" => 5 + rng.Next(20),
                "late" => 70 + rng.Next(25),
                _ => 30 + rng.Next(35),
            };
            while (!ticks.Add(tick)) tick++;
            string text = json
                .Replace("{region}", Escape(region))
                .Replace("{city}", Escape(city))
                .Replace("{village}", Escape(Pick(rng, villages.ToArray())))
                .Replace("{hero}", Escape(hero))
                .Replace("{enemy}", Escape(enemies[0].Name))
                .Replace("{enemy2}", Escape(enemies[^1].Name));
            var ev = JsonSerializer.Deserialize<EventDefinition>(text, Json.Options)
                     ?? throw new InvalidDataException("Bad sandbox event template.");
            ev.Tick = tick;
            events.Add(ev);
        }
        return events.OrderBy(e => e.Tick).ToList();
    }

    internal static Offensive Offensive(Rng rng, string enemy, DateOnly start, int tick, int duration, double strength)
    {
        string season = start.AddDays(7 * tick).Month switch
        {
            3 or 4 or 5 => "spring",
            6 or 7 or 8 => "summer",
            9 or 10 or 11 => "autumn",
            _ => "winter",
        };
        return new Offensive
        {
            Name = $"The {season} offensive of the {enemy}",
            Tick = tick,
            Duration = duration,
            Strength = Math.Round(strength * (0.8 + 0.4 * rng.NextDouble()), 1),
        };
    }

    private static List<(double X, double Y)> SpacedPoints(Rng rng, int count, double width, double height, double minDistance)
    {
        var points = new List<(double X, double Y)>();
        int attempts = 0;
        while (points.Count < count)
        {
            var p = (X: rng.NextDouble() * width, Y: rng.NextDouble() * height);
            if (points.All(q => Dist(p, q) >= minDistance)) points.Add(p);
            if (++attempts % 2000 == 0) minDistance *= 0.9; // crowded: relax spacing
        }
        return points;
    }

    private static List<int> PickCities(Rng rng, List<(double X, double Y)> points, int count, double width)
    {
        // Cities sit away from the edges, and far from each other.
        var interior = Enumerable.Range(0, points.Count).Where(i => points[i].X > width * 0.2 && points[i].X < width * 0.8).ToList();
        if (interior.Count == 0) interior = Enumerable.Range(0, points.Count).ToList();
        var cities = new List<int> { interior[rng.Next(interior.Count)] };
        while (cities.Count < count)
            cities.Add(Enumerable.Range(0, points.Count).Where(i => !cities.Contains(i))
                .OrderByDescending(i => cities.Min(c => Dist(points[i], points[c])) + rng.NextDouble() * width * 0.2).First());
        return cities;
    }

    internal static string Name(Rng rng, HashSet<string> used)
    {
        while (true)
        {
            string name = Pick(rng, Starts) + Pick(rng, Middles) + Pick(rng, Ends);
            name = char.ToUpperInvariant(name[0]) + name[1..];
            if (name.Length >= 4 && used.Add(name)) return name;
        }
    }

    private static void AddTech(CommuneDefinition c, string tech)
    {
        if (!c.Techs.Contains(tech)) c.Techs = c.Techs.Append(tech).ToArray();
    }

    internal static T Pick<T>(Rng rng, T[] items) => items[rng.Next(items.Length)];

    private static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static string Escape(string s) => JsonEncodedText.Encode(s, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString();
}
