using Politicom.Sim.Systems;

namespace Politicom.Sim;

/// <summary>
/// The whole simulation state. Deterministic given a scenario, balance and seed.
/// Each <see cref="Step"/> is one turn (a week unless the scenario says otherwise).
/// </summary>
public sealed class World
{
    private readonly List<Commune> _communes;
    private readonly List<GameEvent> _log = new();
    private readonly Dictionary<string, double> _techShare = new();

    internal double PendingDriftBump;
    internal int NextProjectId = 1;
    internal readonly HashSet<string> WidespreadTechs = new();

    private World(ScenarioDefinition scenario, TechCatalog techs, Balance balance, ulong seed, List<Commune> communes, TrustNetwork trust)
    {
        Scenario = scenario;
        Techs = techs;
        Balance = balance;
        Seed = seed;
        Rng = new Rng(seed);
        _communes = communes;
        Trust = trust;
        Enemies = scenario.Enemies;
        Ally = scenario.Ally;
        Influence = balance.StartInfluence;
        foreach (var e in Enemies) e.EffectiveStrength = e.Strength;
        InitialCommuneCount = communes.Count;
    }

    public ScenarioDefinition Scenario { get; }
    public TechCatalog Techs { get; }
    public Balance Balance { get; }
    public ulong Seed { get; }
    public Rng Rng { get; }
    public IReadOnlyList<Commune> Communes => _communes;
    public TrustNetwork Trust { get; }
    public IReadOnlyList<HostileState> Enemies { get; }
    /// <summary>Spread score input: mean radicalization behind enemy lines.</summary>
    public double Radicalization => Enemies.Count == 0 ? 0 : Enemies.Average(e => e.Radicalization);
    public bool OffensiveUnderway => Enemies.Any(e => e.CurrentOffensive != null);
    public AllyState Ally { get; }
    public int InitialCommuneCount { get; }

    public int Tick { get; private set; }
    public DateOnly Date => DateAt(Tick);
    public DateOnly DateAt(int tick) => Scenario.StartDate.AddDays(Scenario.DaysPerTick * tick);
    /// <summary>What one turn is called in this scenario: "week", "day" or "turn".</summary>
    public string Turn => Scenario.DaysPerTick switch { 7 => "week", 1 => "day", _ => "turn" };
    public string Turns(int n) => $"{n} {Turn}{(n == 1 ? "" : "s")}";
    public double Influence { get; internal set; }
    public DriftTracker Drift { get; } = new();
    public DriftBreakdown LastDrift { get; internal set; } = new();
    public List<Project> Projects { get; } = new();
    public List<PendingSecession> PendingSecessions { get; } = new();
    public List<PendingEvent> PendingEvents { get; } = new();
    public HashSet<string> FiredEvents { get; } = new();
    public bool WarCommittee { get; internal set; }
    public bool ForeignTrade { get; internal set; }
    /// <summary>The drift threshold most recently warned about (0 = none), so warnings don't repeat.</summary>
    public double DriftWarnedAt { get; internal set; }
    /// <summary>Last turn's trade: food sold and goods bought.</summary>
    public double LastExports { get; internal set; }
    public double LastImports { get; internal set; }
    /// <summary>0..0.9: how much of the outside world refuses to trade, as the idea spreads.</summary>
    public double Embargo => Math.Clamp(Radicalization * Balance.EmbargoPerRadicalization, 0, 0.9);
    public int RecallPenaltyTicks { get; internal set; }
    public int LastCongressTick { get; internal set; } = -999;
    /// <summary>Technologies the player is circulating, with ticks remaining.</summary>
    public Dictionary<string, int> IdeaNudges { get; } = new();
    /// <summary>0..0.6 reduction in pooled-goods loss from completed railways.</summary>
    public double TransportBonus { get; internal set; }
    public Scores Scores { get; private set; } = new();
    public Outcome Outcome { get; private set; } = Outcome.Ongoing;
    public IReadOnlyList<GameEvent> Log => _log;
    public List<WeekRecord> History { get; } = new();
    public double InfluenceRegen => InfluenceSystem.Regen(this);

    public IEnumerable<Commune> Active => _communes.Where(c => !c.Occupied);
    public IEnumerable<Commune> Federation => _communes.Where(c => c.InFederation);
    public IEnumerable<Project> ActiveProjects => Projects.Where(p => p.Active);

    public static World Create(ScenarioDefinition scenario, TechCatalog techs, Balance? balance = null, ulong seed = 1)
    {
        balance ??= new Balance();
        // The world mutates its enemy and ally, so it works on its own copy of the scenario.
        scenario = System.Text.Json.JsonSerializer.Deserialize<ScenarioDefinition>(
            System.Text.Json.JsonSerializer.Serialize(scenario, Json.Options), Json.Options)!;
        var rng = new Rng(seed ^ 0x9E3779B97F4A7C15UL);
        var communes = new List<Commune>();
        foreach (var d in scenario.Communes)
        {
            var c = new Commune(communes.Count, d.Name, d.Kind)
            {
                Description = d.Description,
                Longitude = d.Lon,
                Latitude = d.Lat,
                Population = d.Population,
                Values = new CommuneValues(d.Collectivism, d.Militancy, d.Federalism),
                PracticalEducation = d.Practical,
                PoliticalEducation = d.Political,
                PracticalSchooling = balance.BaselineSchooling,
                PoliticalSchooling = balance.BaselineSchooling,
            };
            c.Food = c.Population * balance.FoodNeed * balance.ReserveTicks * 1.5;
            c.Goods = c.Population * balance.GoodsNeed * balance.ReserveTicks * 1.5;
            foreach (var name in d.FrontOf)
            {
                int k = scenario.Enemies.FindIndex(e => e.Name == name);
                if (k < 0) throw new InvalidDataException($"{d.Name} is on the front of unknown enemy '{name}'.");
                c.Fronts.Add(k);
            }
            foreach (var t in d.Techs)
            {
                if (!techs.Contains(t)) throw new InvalidDataException($"{d.Name} starts with unknown tech '{t}'.");
                c.Techs.Add(t);
            }
            communes.Add(c);
        }
        NormalizeMap(communes);
        foreach (var ev in scenario.Events)
        {
            if (ev.Options.Count == 0 || ev.DefaultOption < 0 || ev.DefaultOption >= ev.Options.Count)
                throw new InvalidDataException($"Event '{ev.Id}' needs options and a valid defaultOption.");
            foreach (var o in ev.Options)
                if (o.Effects.Enemy.Length > 0 && scenario.Enemies.All(e => e.Name != o.Effects.Enemy))
                    throw new InvalidDataException($"Event '{ev.Id}' names unknown enemy '{o.Effects.Enemy}'.");
            var names = ev.RequiresFederated.Concat(ev.Options.SelectMany(o => o.Effects.Communes.Concat(o.Effects.Secede)));
            foreach (var name in names)
                if (communes.All(c => c.Name != name)) throw new InvalidDataException($"Event '{ev.Id}' names unknown commune '{name}'.");
        }

        // Initial trust: shared values and shared kind of work make neighbours trust each other more.
        var trust = TrustNetwork.Build(communes, scenario.NeighborCount, scenario.NeighborRadiusKm, (a, b) =>
            Math.Clamp(0.4 + 0.25 * (1 - a.Values.Distance(b.Values) * 2) + (a.Kind == b.Kind ? 0.05 : 0) + rng.Jitter(0.05), 0.1, 0.9));

        var world = new World(scenario, techs, balance, seed, communes, trust);
        world.RefreshTechShares();
        world.Emit(Severity.Info, scenario.Description);
        return world;
    }

    public static World Load(string dataDir, string scenarioId, Balance? balance = null, ulong seed = 1) =>
        Create(ScenarioDefinition.Resolve(dataDir, scenarioId), TechCatalog.Load(DataPaths.Technologies(dataDir)), balance, seed);

    /// <summary>Snapshot of the full game state.</summary>
    public SaveGame Save() => new()
    {
        SavedAtUtc = DateTime.UtcNow,
        Scenario = Scenario,
        Balance = Balance,
        Seed = Seed,
        RngState = Rng.State,
        Tick = Tick,
        Influence = Influence,
        DriftValue = Drift.Value,
        DriftStage = Drift.Stage,
        LastDrift = LastDrift,
        PendingDriftBump = PendingDriftBump,
        Communes = _communes,
        Neighbors = Trust.ExportAdjacency(),
        Trust = Trust.Edges().Select(e => new TrustEdge(e.A, e.B, Trust[e.A, e.B])).ToList(),
        Projects = Projects,
        NextProjectId = NextProjectId,
        PendingSecessions = PendingSecessions,
        PendingEvents = PendingEvents,
        FiredEvents = FiredEvents.ToList(),
        WarCommittee = WarCommittee,
        ForeignTrade = ForeignTrade,
        DriftWarnedAt = DriftWarnedAt,
        RecallPenaltyTicks = RecallPenaltyTicks,
        LastCongressTick = LastCongressTick,
        IdeaNudges = IdeaNudges,
        TransportBonus = TransportBonus,
        WidespreadTechs = WidespreadTechs.ToList(),
        Scores = Scores,
        Outcome = Outcome,
        InitialCommuneCount = InitialCommuneCount,
        Log = _log,
        History = History,
    };

    public string SaveJson() => Save().ToJson();

    public static World LoadJson(string json, TechCatalog techs) => Restore(SaveGame.FromJson(json), techs);

    /// <summary>Rebuilds a world from a save. The result continues exactly as the saved game would have.</summary>
    public static World Restore(SaveGame s, TechCatalog techs)
    {
        if (s.Communes.Count != s.InitialCommuneCount)
            throw new InvalidDataException("Save file is inconsistent: commune count changed.");
        for (int i = 0; i < s.Communes.Count; i++)
        {
            if (s.Communes[i].Id != i) throw new InvalidDataException("Save file is inconsistent: commune ids out of order.");
            foreach (var t in s.Communes[i].Techs)
                if (!techs.Contains(t)) throw new InvalidDataException($"Save file uses technology '{t}', which is not in the data files.");
        }

        if (s.Neighbors.Count != s.Communes.Count) throw new InvalidDataException("Save file is inconsistent: trust network size.");
        var trust = TrustNetwork.Import(s.Neighbors, s.Trust);

        var w = new World(s.Scenario, techs, s.Balance, s.Seed, s.Communes, trust)
        {
            Tick = s.Tick,
            Influence = s.Influence,
            LastDrift = s.LastDrift,
            PendingDriftBump = s.PendingDriftBump,
            NextProjectId = s.NextProjectId,
            WarCommittee = s.WarCommittee,
            ForeignTrade = s.ForeignTrade,
            DriftWarnedAt = s.DriftWarnedAt,
            RecallPenaltyTicks = s.RecallPenaltyTicks,
            LastCongressTick = s.LastCongressTick,
            TransportBonus = s.TransportBonus,
            Scores = s.Scores,
            Outcome = s.Outcome,
        };
        w.Rng.State = s.RngState;
        w.Drift.Restore(s.DriftValue, s.DriftStage);
        w.Projects.AddRange(s.Projects);
        w.PendingSecessions.AddRange(s.PendingSecessions);
        w.PendingEvents.AddRange(s.PendingEvents);
        w.FiredEvents.UnionWith(s.FiredEvents);
        foreach (var (k, v) in s.IdeaNudges) w.IdeaNudges[k] = v;
        w.WidespreadTechs.UnionWith(s.WidespreadTechs);
        w._log.AddRange(s.Log);
        w.History.AddRange(s.History);
        w.RefreshTechShares();
        return w;
    }

    public ActionResult Apply(PlayerAction action) =>
        Outcome != Outcome.Ongoing ? ActionResult.Fail("The game is over.") : ActionSystem.Apply(this, action);

    /// <summary>Advances one turn.</summary>
    public void Step()
    {
        if (Outcome != Outcome.Ongoing) return;
        RefreshTechShares();
        EventSystem.Run(this);
        EconomySystem.Run(this);
        EducationSystem.Run(this);
        ResearchSystem.Run(this);
        ProjectSystem.Run(this);
        WarSystem.Run(this);
        TrustSystem.Run(this);
        FederationSystem.Run(this);
        DriftSystem.Run(this);
        InfluenceSystem.Run(this);
        ScoreSystem.Run(this);
        RecordWeek();
        Tick++;
        CheckOutcome();
    }

    public void Run(int ticks)
    {
        for (int i = 0; i < ticks && Outcome == Outcome.Ongoing; i++) Step();
    }

    /// <summary>Share of federated communes that hold a technology.</summary>
    public double TechShare(string id) => _techShare.GetValueOrDefault(id);

    /// <summary>Sum of a society-wide effect, weighted by how widely each technology is held.</summary>
    public double SocietyEffect(Func<TechEffects, double> effect)
    {
        double sum = 0;
        foreach (var t in Techs.All) sum += effect(t.Effects) * TechShare(t.Id);
        return sum;
    }

    /// <summary>Sum of a local effect over the technologies one commune holds.</summary>
    public double LocalEffect(Commune c, Func<TechEffects, double> effect)
    {
        double sum = 0;
        foreach (var id in c.Techs) sum += effect(Techs[id].Effects);
        return sum;
    }

    public EventDefinition EventDefinition(string id) => Scenario.Events.First(e => e.Id == id);

    public int ProjectsJoinedBy(int commune) => Projects.Count(p => p.Active && p.Participants.Contains(commune));

    public double Mean(Func<Commune, double> f, IEnumerable<Commune>? among = null)
    {
        var list = (among ?? Federation).ToList();
        return list.Count == 0 ? 0 : list.Average(f);
    }

    internal void Emit(Severity severity, string text, int? commune = null, bool pause = false) =>
        _log.Add(new GameEvent(Tick, Date, severity, text, commune, pause));

    /// <summary>One-off drift from an event; applied in this tick's drift update.</summary>
    internal void BumpDrift(double amount) => PendingDriftBump += amount;

    internal void RefreshTechShares()
    {
        var members = Federation.ToList();
        foreach (var t in Techs.All)
            _techShare[t.Id] = members.Count == 0 ? 0 : members.Count(c => c.Techs.Contains(t.Id)) / (double)members.Count;
    }

    private void RecordWeek()
    {
        var edges = Trust.Edges().Where(e => Communes[e.A].InFederation && Communes[e.B].InFederation).ToList();
        History.Add(new WeekRecord(Tick, Drift.Value, Federation.Count(), _communes.Count(c => c.Occupied),
            _communes.Count(c => !c.Occupied && !c.Federated),
            edges.Count == 0 ? 0 : edges.Average(e => Trust[e.A, e.B]), Influence, Enemies.Sum(e => e.EffectiveStrength)));
    }

    private void CheckOutcome()
    {
        if (Drift.Stage == DriftStage.State)
        {
            Outcome = Outcome.BecameState;
            Emit(Severity.Danger, "A state has crystallized. The society lives on, but the revolution is over.");
        }
        else if (Federation.Count() < Balance.CollapseFraction * InitialCommuneCount)
        {
            Outcome = Outcome.Destroyed;
            int occupied = _communes.Count(c => c.Occupied);
            int seceded = _communes.Count(c => !c.Occupied && !c.Federated);
            Emit(Severity.Danger, seceded > occupied
                ? "Most communes have gone their own way. They are still free, but the federation has dissolved."
                : "Too few free communes remain. The federation has been destroyed.");
        }
        else if (Tick >= Scenario.EndTick)
        {
            Outcome = Outcome.Survived;
            Emit(Severity.Good, $"The federation endured to {Date:MMMM yyyy}.");
        }
    }

    /// <summary>Projects lon/lat to a 0..1 box preserving aspect; north up.</summary>
    private static void NormalizeMap(List<Commune> communes)
    {
        double meanLat = communes.Average(c => c.Latitude) * Math.PI / 180;
        double xs(Commune c) => c.Longitude * Math.Cos(meanLat);
        double minX = communes.Min(xs), maxX = communes.Max(xs);
        double minY = communes.Min(c => c.Latitude), maxY = communes.Max(c => c.Latitude);
        double span = Math.Max(maxX - minX, maxY - minY);
        foreach (var c in communes)
        {
            c.X = (xs(c) - minX) / span;
            c.Y = (maxY - c.Latitude) / span;
        }
    }
}
