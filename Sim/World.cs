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
    /// <summary>The calendar year, which gates the technologies of each age.</summary>
    public int Year => Date.Year + Scenario.YearOffset;
    /// <summary>
    /// How fast ideas travel in this age: by word of mouth before the printing press, by print and
    /// post before the telegraph. Scales contagion and the spread of techniques.
    /// </summary>
    public double CommunicationSpeed => Year < 1450 ? 0.6 : Year < 1800 ? 0.8 : 1;

    /// <summary>A date in this game's calendar, e.g. Format(Date, "MMMM yyyy") → "March 499 BC".</summary>
    public string Format(DateOnly date, string format) => Ages.Format(date, Scenario.YearOffset, format);

    /// <summary>Common knowledge by now: no longer a choice, not shown, and it satisfies prerequisites.</summary>
    public bool CommonKnowledge(TechDefinition t) => t.Until is int until && Year >= until;
    /// <summary>The world has reached the technology's era, so it can be discovered.</summary>
    public bool Reached(TechDefinition t) => t.Era <= Year;
    /// <summary>The technologies of this game: not yet common knowledge. Later ones are shown but locked.</summary>
    public IEnumerable<TechDefinition> LiveTechs => Techs.All.Where(t => !CommonKnowledge(t));
    /// <summary>Whether a commune can build on a technology: it holds it, or everyone knows it.</summary>
    public bool Knows(Commune c, string id) => c.Techs.Contains(id) || CommonKnowledge(Techs[id]);

    /// <summary>Line the movement asks the communes to put their science into (null = balanced).</summary>
    public string? ResearchFocus { get; internal set; }
    /// <summary>Science accumulated toward the next breakthrough.</summary>
    public double Science { get; internal set; }
    /// <summary>Science produced last turn, across the federation.</summary>
    public double LastScience { get; internal set; }
    public int Breakthroughs { get; internal set; }
    /// <summary>Turn of the last random event.</summary>
    public int LastRandomEvent { get; internal set; } = -999;
    /// <summary>Cities of a regional rising's country that have risen and joined.</summary>
    public HashSet<string> Risen { get; } = new();
    /// <summary>Science the next breakthrough needs.</summary>
    public double BreakthroughCost => ResearchSystem.BreakthroughCost(this);
    /// <summary>The federation's technology level when the game started, for comparing with the world's.</summary>
    public double TechLevelStart { get; internal set; }
    /// <summary>
    /// The federation's technology level, 0..1: the share of this age's reached technologies the
    /// average commune holds.
    /// </summary>
    public double TechLevel
    {
        get
        {
            var reached = LiveTechs.Where(Reached).Select(t => t.Id).ToList();
            var members = Federation.ToList();
            if (reached.Count == 0 || members.Count == 0) return 0;
            return members.Average(c => reached.Count(c.Techs.Contains)) / reached.Count;
        }
    }
    public DateOnly DateAt(int tick) => Scenario.StartDate.AddDays(Scenario.DaysPerTick * tick);
    /// <summary>What one turn is called in this scenario: "week", "day" or "turn".</summary>
    /// <summary>A turn's length in weeks: slow processes (science, culture, modernization) scale with it.</summary>
    public double Weeks => Scenario.DaysPerTick / 7.0;
    public string Turn => Scenario.DaysPerTick switch { 7 => "week", 1 => "day", _ => "turn" };
    public string Turns(int n) => $"{n} {Turn}{(n == 1 ? "" : "s")}";
    public double Influence { get; internal set; }
    public DriftTracker Drift { get; } = new();
    public DriftBreakdown LastDrift { get; internal set; } = new();
    public List<Project> Projects { get; } = new();
    public List<PendingSecession> PendingSecessions { get; } = new();
    public List<PendingEvent> PendingEvents { get; } = new();
    public HashSet<string> FiredEvents { get; } = new();
    /// <summary>Railway lines between communes, each as (smaller id, larger id).</summary>
    public HashSet<(int A, int B)> Railways { get; } = new();

    public bool Rail(int a, int b) => Railways.Contains(a < b ? (a, b) : (b, a));

    internal void AddRailway(int a, int b)
    {
        if (a == b) return;
        if (!Trust.Connected(a, b)) Trust.Link(a, b, 0.5);
        Railways.Add(a < b ? (a, b) : (b, a));
    }

    public bool WarCommittee { get; internal set; }
    public bool ForeignTrade { get; internal set; }
    /// <summary>The drift threshold most recently warned about (0 = none), so warnings don't repeat.</summary>
    public double DriftWarnedAt { get; internal set; }
    /// <summary>Last turn's trade: food sold and goods bought.</summary>
    public double LastExports { get; internal set; }
    public double LastImports { get; internal set; }
    /// <summary>0..0.9: how much of the outside world refuses to trade, as the idea spreads.</summary>
    public double Embargo => States.Count > 0
        ? Math.Clamp(Global.DiplomacySystem.Embargo(this), 0, 0.9)
        : Math.Clamp(Radicalization * Balance.EmbargoPerRadicalization, 0, 0.9);

    /// <summary>The outside world's states (empty for maps without a world).</summary>
    public List<Global.WorldState> States { get; } = new();
    public List<Global.War> Wars { get; } = new();
    private readonly Dictionary<string, double> _baselines = new();
    /// <summary>Where a state's stance settles absent anything else: its starting attitude.</summary>
    public double Baseline(string stateId) => _baselines.GetValueOrDefault(stateId);
    internal void SetBaseline(string stateId, double value) => _baselines[stateId] = value;

    /// <summary>
    /// New communes join mid-game (a sister federation's cities). They link to each other and,
    /// weakly, to the nearest commune already in the federation.
    /// </summary>
    internal void AddCommunes(List<CommuneDefinition> defs)
    {
        int first = _communes.Count;
        foreach (var d in defs)
        {
            var c = new Commune(_communes.Count, d.Name, d.Kind)
            {
                Description = d.Description, Longitude = d.Lon, Latitude = d.Lat, Population = d.Population,
                Values = new CommuneValues(d.Collectivism, d.Militancy, d.Federalism),
                PracticalEducation = d.Practical, PoliticalEducation = d.Political,
                PracticalSchooling = Balance.BaselineSchooling, PoliticalSchooling = Balance.BaselineSchooling,
                Wellbeing = 0.65,
            };
            c.Food = c.Population * Balance.FoodNeed * Balance.ReserveTicks * 1.5;
            c.Goods = c.Population * Balance.GoodsNeed * Balance.ReserveTicks * 1.5;
            c.Techs.UnionWith(d.Techs);
            _communes.Add(c);
        }
        Trust.Grow(defs.Count);
        var added = _communes.Skip(first).ToList();
        foreach (var c in added)
            foreach (var n in added.Where(o => o != c).OrderBy(o => TrustNetwork.DistanceKm(c, o)).Take(3))
                Trust.Link(c.Id, n.Id, 0.6);
        var anchor = added.SelectMany(c => _communes.Take(first).Where(o => o.InFederation).Select(o => (c, o)))
                          .OrderBy(p => TrustNetwork.DistanceKm(p.c, p.o)).FirstOrDefault();
        if (anchor.c != null) Trust.Link(anchor.c.Id, anchor.o.Id, 0.4);
        RefreshTechShares();
    }
    /// <summary>Last turn's arms bought from friendly states.</summary>
    public double LastArms { get; internal set; }
    /// <summary>This turn's defense bonus from foreign volunteers.</summary>
    public double VolunteerDefense { get; internal set; }
    public int AppealTicks { get; internal set; }
    public int LastAppealTick { get; internal set; } = -999;
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
        foreach (var d in scenario.States)
        {
            world.States.Add(new Global.WorldState
            {
                Id = d.Id, Name = d.Name, Ideology = d.Ideology, Power = d.Power, Proximity = d.Proximity,
                Neighbours = d.Neighbours, Stance = d.InitialStance, TechLevel = Global.TechLevelSystem.Initial(d),
            });
            world._baselines[d.Id] = d.InitialStance;
        }
        foreach (var line in scenario.Railways)
        {
            if (line.Length != 2) throw new InvalidDataException("Each railway is a pair of commune names.");
            int Id(string name) => communes.FirstOrDefault(c => c.Name == name)?.Id
                                   ?? throw new InvalidDataException($"Railway names unknown commune '{name}'.");
            world.AddRailway(Id(line[0]), Id(line[1]));
        }
        world.RefreshTechShares();
        foreach (var c in world._communes) c.Culture = balance.StartCulture * c.PoliticalEducation;
        world.TechLevelStart = world.TechLevel;
        foreach (var e in world.Enemies) e.TechStart = e.TechLevel;
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
        Railways = Railways.Select(r => new[] { r.A, r.B }).ToList(),
        Neighbors = Trust.ExportAdjacency(),
        Trust = Trust.Edges().Select(e => new TrustEdge(e.A, e.B, Trust[e.A, e.B])).ToList(),
        Projects = Projects,
        NextProjectId = NextProjectId,
        PendingSecessions = PendingSecessions,
        PendingEvents = PendingEvents,
        FiredEvents = FiredEvents.ToList(),
        WarCommittee = WarCommittee,
        ForeignTrade = ForeignTrade,
        States = States,
        Wars = Wars,
        AppealTicks = AppealTicks,
        LastAppealTick = LastAppealTick,
        DriftWarnedAt = DriftWarnedAt,
        RecallPenaltyTicks = RecallPenaltyTicks,
        LastCongressTick = LastCongressTick,
        IdeaNudges = IdeaNudges,
        TransportBonus = TransportBonus,
        WidespreadTechs = WidespreadTechs.ToList(),
        ResearchFocus = ResearchFocus,
        Science = Science,
        LastScience = LastScience,
        Breakthroughs = Breakthroughs,
        TechLevelStart = TechLevelStart,
        LastRandomEvent = LastRandomEvent,
        Risen = Risen.ToList(),
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
        if (s.Communes.Count < s.InitialCommuneCount)
            throw new InvalidDataException("Save file is inconsistent: communes are missing.");
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
            AppealTicks = s.AppealTicks,
            LastAppealTick = s.LastAppealTick,
            DriftWarnedAt = s.DriftWarnedAt,
            RecallPenaltyTicks = s.RecallPenaltyTicks,
            LastCongressTick = s.LastCongressTick,
            TransportBonus = s.TransportBonus,
            ResearchFocus = s.ResearchFocus,
            Science = s.Science,
            LastScience = s.LastScience,
            Breakthroughs = s.Breakthroughs,
            TechLevelStart = s.TechLevelStart,
            LastRandomEvent = s.LastRandomEvent,
            Scores = s.Scores,
            Outcome = s.Outcome,
        };
        w.Rng.State = s.RngState;
        w.States.AddRange(s.States);
        w.Wars.AddRange(s.Wars);
        foreach (var d in s.Scenario.States) w._baselines[d.Id] = d.InitialStance;
        foreach (var r in s.Railways) w.Railways.Add((Math.Min(r[0], r[1]), Math.Max(r[0], r[1])));
        w.Drift.Restore(s.DriftValue, s.DriftStage);
        w.Projects.AddRange(s.Projects);
        w.PendingSecessions.AddRange(s.PendingSecessions);
        w.PendingEvents.AddRange(s.PendingEvents);
        w.FiredEvents.UnionWith(s.FiredEvents);
        foreach (var (k, v) in s.IdeaNudges) w.IdeaNudges[k] = v;
        w.WidespreadTechs.UnionWith(s.WidespreadTechs);
        w.Risen.UnionWith(s.Risen);
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
        RandomEventSystem.Run(this);
        EconomySystem.Run(this);
        EducationSystem.Run(this);
        ResearchSystem.Run(this);
        CultureSystem.Run(this);
        ProjectSystem.Run(this);
        Global.DiplomacySystem.Run(this);
        Global.RevolutionSystem.Run(this);
        Global.InterstateSystem.Run(this);
        Global.RivalSystem.Run(this);
        Global.TechLevelSystem.Run(this);
        RisingSystem.Run(this);
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
        foreach (var t in Techs.All)
            if (!CommonKnowledge(t)) sum += effect(t.Effects) * TechShare(t.Id);
        return sum;
    }

    /// <summary>Sum of a local effect over the technologies one commune holds.</summary>
    public double LocalEffect(Commune c, Func<TechEffects, double> effect)
    {
        double sum = 0;
        foreach (var id in c.Techs)
            if (Techs[id] is var t && !CommonKnowledge(t)) sum += effect(t.Effects);
        return sum;
    }

    /// <summary>Whether a commune has the prerequisites and education to discover a technology itself.</summary>
    public bool CanDiscover(Commune c, TechDefinition t) => ResearchSystem.CanDiscover(this, c, t);

    public EventDefinition EventDefinition(string id) => Scenario.Events.First(e => e.Id == id);

    public int ProjectsJoinedBy(int commune) => Projects.Count(p => p.Active && p.Participants.Contains(commune));

    public double Mean(Func<Commune, double> f, IEnumerable<Commune>? among = null)
    {
        var list = (among ?? Federation).ToList();
        return list.Count == 0 ? 0 : list.Average(f);
    }

    internal void Emit(Severity severity, string text, int? commune = null, bool pause = false, string title = "") =>
        _log.Add(new GameEvent(Tick, Date, severity, text, commune, pause, title));

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
            Emit(Severity.Good, $"The federation endured to {Format(Date, "MMMM yyyy")}.");
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
