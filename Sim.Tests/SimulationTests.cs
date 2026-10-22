using Politicom.Sim.Ai;
using Politicom.Sim.Sandbox;

namespace Politicom.Sim.Tests;

public class SimulationTests
{
    private static readonly string DataDir = DataPaths.Find();

    private static World NewWorld(ulong seed = 1, Balance? balance = null) =>
        World.Load(DataDir, "catalonia_1936", balance, seed);

    private static World Play(IPolicy policy, ulong seed, Balance? balance = null)
    {
        var w = NewWorld(seed, balance);
        while (w.Outcome == Outcome.Ongoing)
        {
            policy.Act(w);
            w.Step();
        }
        return w;
    }

    public static TheoryData<string> Scenarios() => new()
    {
        "catalonia_1936",
        "free_territory_1918",
        "paris_commune_1871",
        "sandbox:7",
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Scenario_loads_within_prototype_scope(string scenario)
    {
        var w = World.Load(DataDir, scenario);
        Assert.InRange(w.Communes.Count, 20, 40);
        Assert.Contains(w.Communes, c => c.Front);
        Assert.NotEmpty(w.Enemies);
        Assert.All(w.Communes, c => Assert.NotEmpty(w.Trust.Neighbors(c.Id)));
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Every_scenario_plays_to_an_outcome_and_survives_a_save(string scenario)
    {
        var policy = new Balanced();
        var straight = World.Load(DataDir, scenario, seed: 9);
        var resumed = World.Load(DataDir, scenario, seed: 9);
        for (int i = 0; i < 30; i++)
        {
            policy.Act(resumed);
            resumed.Step();
        }
        resumed = World.LoadJson(resumed.SaveJson(), resumed.Techs);
        foreach (var w in new[] { straight, resumed })
            while (w.Outcome == Outcome.Ongoing)
            {
                policy.Act(w);
                w.Step();
            }
        Assert.NotEqual(Outcome.Ongoing, straight.Outcome);
        Assert.Equal(straight.Log.Select(e => e.Text), resumed.Log.Select(e => e.Text));
    }

    private static readonly Lazy<SandboxGenerator> Sandbox = new(() => SandboxGenerator.Load(DataDir));

    [Fact]
    public void Sandbox_maps_are_deterministic_per_seed()
    {
        string Json(ScenarioDefinition s) => System.Text.Json.JsonSerializer.Serialize(s);
        Assert.Equal(Json(Sandbox.Value.Generate(42)), Json(Sandbox.Value.Generate(42)));
        Assert.NotEqual(Json(Sandbox.Value.Generate(42, calibrate: false)), Json(Sandbox.Value.Generate(43, calibrate: false)));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(26)]
    [InlineData(40)]
    public void Sandbox_maps_are_valid_across_many_seeds(int size)
    {
        var techs = NewWorld().Techs;
        for (ulong seed = 1; seed <= 50; seed++)
        {
            var scenario = Sandbox.Value.Generate(seed, size, calibrate: false);
            Assert.Equal(size, scenario.Communes.Count);
            Assert.Equal(size, scenario.Communes.Select(c => c.Name).Distinct().Count());
            Assert.All(scenario.Enemies, e => Assert.Contains(scenario.Communes, c => c.FrontOf.Contains(e.Name)));
            Assert.Equal(5, scenario.Events.Count);
            var w = World.Create(scenario, techs, seed: seed); // validates event, enemy and tech references
            Assert.All(w.Communes, c => Assert.NotEmpty(w.Trust.Neighbors(c.Id)));
        }
    }

    [Fact]
    public void Sandbox_ids_parse()
    {
        Assert.True(SandboxGenerator.TryParseId("sandbox:42", out var seed, out var size));
        Assert.Equal((42UL, SandboxGenerator.DefaultSize), (seed, size));
        Assert.True(SandboxGenerator.TryParseId("sandbox:7:32", out seed, out size));
        Assert.Equal((7UL, 32), (seed, size));
        Assert.False(SandboxGenerator.TryParseId("catalonia_1936", out _, out _));
        Assert.False(SandboxGenerator.TryParseId("sandbox:x", out _, out _));
    }

    /// <summary>Calibration should make every map winnable but hard for the balanced player, and passive play a loss.</summary>
    [Fact]
    public void Calibrated_sandbox_maps_are_winnable_but_hard()
    {
        var techs = NewWorld().Techs;
        foreach (ulong map in new ulong[] { 3, 11, 29 })
        {
            var scenario = Sandbox.Value.Generate(map);
            int Survivals(IPolicy policy) => Enumerable.Range(100, 10).Count(g =>
            {
                var w = World.Create(scenario, techs, seed: (ulong)g);
                while (w.Outcome == Outcome.Ongoing)
                {
                    policy.Act(w);
                    w.Step();
                }
                return w.Outcome == Outcome.Survived;
            });
            int balanced = Survivals(new Balanced());
            Assert.InRange(balanced, 2, 10);
            Assert.True(Survivals(new Passive()) < balanced, $"map {map}: passive should do worse than balanced");
        }
    }

    [Fact]
    public void Turn_length_follows_the_scenario()
    {
        var paris = World.Load(DataDir, "paris_commune_1871");
        var catalonia = NewWorld();
        paris.Run(10);
        catalonia.Run(10);
        Assert.Equal(new DateOnly(1871, 3, 28), paris.Date);
        Assert.Equal(new DateOnly(1936, 9, 27), catalonia.Date);
        Assert.Equal("day", paris.Turn);
        Assert.Equal("week", catalonia.Turn);
        Assert.Equal("3 days", paris.Turns(3));
    }

    [Fact]
    public void Imports_feed_a_city_that_cannot_feed_itself()
    {
        var fed = World.Load(DataDir, "paris_commune_1871");
        var blockaded = ScenarioDefinition.Load(DataPaths.Scenario(DataDir, "paris_commune_1871"));
        blockaded.FoodImports = 0;
        var cut = World.Create(blockaded, fed.Techs);
        fed.Run(20);
        cut.Run(20);
        Assert.True(cut.Communes.Count(c => c.Starving) > fed.Communes.Count(c => c.Starving));
    }

    [Fact]
    public void Occupation_is_credited_to_the_enemy_pressing_hardest()
    {
        var w = World.Load(DataDir, "free_territory_1918", seed: 3);
        w.Run(140);
        var occupied = w.Communes.Where(c => c.Occupied).ToList();
        Assert.NotEmpty(occupied);
        Assert.All(occupied, c => Assert.NotNull(c.OccupiedBy));
        Assert.Contains(occupied, c => w.Enemies[c.OccupiedBy!.Value].Name == "Red Army");
        Assert.Contains(occupied, c => w.Enemies[c.OccupiedBy!.Value].Name == "White Army");
    }

    [Fact]
    public void Same_seed_replays_identically()
    {
        var a = Play(new Balanced(), 42);
        var b = Play(new Balanced(), 42);
        Assert.Equal(a.Tick, b.Tick);
        Assert.Equal(a.Drift.Value, b.Drift.Value);
        Assert.Equal(a.Scores.Total, b.Scores.Total);
        Assert.Equal(a.Log.Select(e => e.Text), b.Log.Select(e => e.Text));
    }

    [Fact]
    public void Different_seeds_diverge()
    {
        var a = Play(new Passive(), 1);
        var b = Play(new Passive(), 2);
        Assert.NotEqual(a.Log.Select(e => e.Text), b.Log.Select(e => e.Text));
    }

    [Fact]
    public void Trust_stays_in_bounds_over_a_full_game()
    {
        var w = Play(new Centralizer(), 7);
        Assert.All(w.Trust.Edges(), e => Assert.InRange(w.Trust[e.A, e.B], 0, 1));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void Coordination_cost_rises_with_participants(double political)
    {
        var b = new Balance();
        double previous = -1;
        for (int n = 1; n <= 30; n++)
        {
            double cost = Coordination.Cost(n, political, 0, DriftStage.Free, false, b);
            Assert.InRange(cost, 0, 1);
            Assert.True(cost >= previous, $"cost fell at n={n}");
            previous = cost;
        }
        Assert.Equal(0, Coordination.Cost(1, political, 0, DriftStage.Free, false, b));
    }

    [Fact]
    public void Education_and_technology_reduce_coordination_cost()
    {
        var b = new Balance();
        double raw = Coordination.Cost(12, 0, 0, DriftStage.Free, false, b);
        Assert.True(Coordination.Cost(12, 0.8, 0, DriftStage.Free, false, b) < raw);
        Assert.True(Coordination.Cost(12, 0, 0.45, DriftStage.Free, false, b) < raw);
        Assert.True(Coordination.Cost(12, 0, -0.15, DriftStage.Free, false, b) > raw);
    }

    [Fact]
    public void Drift_stages_rise_at_thresholds_and_fall_only_with_hysteresis()
    {
        var d = new DriftTracker();
        d.Apply(26, 10);
        Assert.Equal(DriftStage.PermanentCouncils, d.Stage);
        d.Apply(-5, 10); // 21: below 25 but within hysteresis
        Assert.Equal(DriftStage.PermanentCouncils, d.Stage);
        d.Apply(-7, 10); // 14: more than 10 below 25
        Assert.Equal(DriftStage.Free, d.Stage);
        d.Apply(60, 10); // 74
        Assert.Equal(DriftStage.Commissars, d.Stage);
    }

    [Fact]
    public void Becoming_a_state_is_irreversible_and_ends_the_game()
    {
        var d = new DriftTracker();
        d.Apply(100, 10);
        Assert.Equal(DriftStage.State, d.Stage);
        d.Apply(-100, 10);
        Assert.Equal(DriftStage.State, d.Stage);
        Assert.Equal(100, d.Value);
    }

    [Fact]
    public void Expert_trap_adds_drift_when_practical_outruns_political()
    {
        var even = NewWorld();
        var trap = NewWorld();
        foreach (var c in trap.Communes)
        {
            c.PracticalEducation = 0.8;
            c.PoliticalEducation = 0.1;
        }
        even.Step();
        trap.Step();
        Assert.True(trap.LastDrift.ExpertTrap > even.LastDrift.ExpertTrap + 0.2);
        Assert.True(trap.Drift.Value > even.Drift.Value);
    }

    [Fact]
    public void War_committee_strengthens_defense_and_raises_drift()
    {
        var off = NewWorld();
        var on = NewWorld();
        on.Apply(new SetWarCommittee(true));
        off.Step();
        on.Step();
        var front = off.Communes.First(c => c.Front).Id;
        Assert.True(on.Communes[front].Defense > off.Communes[front].Defense);
        Assert.True(on.Drift.Value > off.Drift.Value);
    }

    [Fact]
    public void Foreign_trade_turns_grain_into_arms_at_the_cost_of_drift()
    {
        var closed = NewWorld();
        var open = NewWorld();
        Assert.True(open.Apply(new SetForeignTrade(true)).Ok);
        closed.Run(3);
        open.Run(3);
        Assert.True(open.LastExports > 0);
        Assert.True(open.LastImports > 0);
        Assert.True(open.LastDrift.ForeignTrade > 0);
        Assert.True(open.Drift.Value > closed.Drift.Value);
        var front = closed.Communes.First(c => c.Front).Id;
        Assert.True(open.Communes[front].Goods >= closed.Communes[front].Goods);
    }

    [Fact]
    public void Spreading_the_idea_brings_embargoes()
    {
        var w = NewWorld();
        w.Apply(new SetForeignTrade(true));
        w.Step();
        double before = w.LastImports / w.LastExports;
        foreach (var e in w.Enemies) e.Radicalization = 0.4;
        Assert.Equal(0.6, w.Embargo, 6);
        w.Step();
        // Trade this turn happened at the 60% embargo: 0.8 goods per grain becomes 0.32.
        Assert.Equal(w.Balance.TradeRate * 0.4, w.LastImports / w.LastExports, 6);
        Assert.True(w.LastImports / w.LastExports < before);
    }

    [Fact]
    public void Receiving_aid_reads_as_free_riding_only_for_individualists()
    {
        var w = NewWorld();
        var comrade = w.Communes[0];
        var loner = w.Communes[1];
        comrade.Values = new CommuneValues(1, 0, 0);
        loner.Values = new CommuneValues(-1, 0, 0);
        foreach (var c in new[] { comrade, loner })
        {
            c.AidReceived = c.Population * 4;
            c.PoliticalEducation = 0;
        }
        w.Step();
        Assert.True(comrade.FreeRiding < 0.01, $"collectivist in need flagged: {comrade.FreeRiding}");
        Assert.True(loner.FreeRiding > 0.3, $"individualist taking aid not flagged: {loner.FreeRiding}");
    }

    [Fact]
    public void A_commune_seceding_through_an_event_does_not_secede_twice()
    {
        var w = NewWorld();
        var c = w.Communes[9];
        w.PendingSecessions.Add(new PendingSecession(c.Id, w.Tick + 2));
        Systems.EventSystem.Apply(w, new EventEffects { Secede = new[] { c.Name } });
        Assert.Empty(w.PendingSecessions);
        w.Run(3);
        Assert.Single(w.Log, e => e.Commune == c.Id && e.Text.Contains("leaves the federation"));
    }

    [Fact]
    public void Ideas_spread_only_to_communes_with_half_the_education_to_use_them()
    {
        var w = NewWorld();
        var mandate = w.Techs["imperative_mandate"]; // needs political education to adopt
        var c = w.Communes[0];
        c.PoliticalEducation = mandate.MinPolitical * 0.4;
        Assert.False(Systems.ResearchSystem.CanAdopt(c, mandate));
        c.PoliticalEducation = mandate.MinPolitical * 0.6;
        Assert.True(Systems.ResearchSystem.CanAdopt(c, mandate));
    }

    [Fact]
    public void Mass_secession_dissolves_the_federation_rather_than_destroying_it()
    {
        var w = NewWorld();
        foreach (var c in w.Communes.Skip(3)) c.Federated = false;
        w.Step();
        Assert.Equal(Outcome.Destroyed, w.Outcome);
        Assert.Contains(w.Log, e => e.Text.Contains("dissolved"));
    }

    [Fact]
    public void Drift_warns_before_a_threshold_and_stops_the_clock_when_a_stage_forms()
    {
        var w = NewWorld();
        w.Apply(new SetWarCommittee(true));
        w.Apply(new SetForeignTrade(true));
        while (w.Drift.Stage == DriftStage.Free && w.Outcome == Outcome.Ongoing) w.Step();
        var warning = Assert.Single(w.Log, e => e.Text.StartsWith("Hierarchy drift is nearing 25"));
        var hardens = Assert.Single(w.Log, e => e.Text.StartsWith("Hierarchy hardens"));
        Assert.True(warning.StopsTheClock);
        Assert.True(hardens.StopsTheClock);
        Assert.True(warning.Tick < hardens.Tick);
        Assert.False(w.Log.First(e => e.Text.StartsWith("A war committee")).StopsTheClock); // ordinary warnings don't
    }

    [Fact]
    public void Mediation_raises_trust_and_costs_influence()
    {
        var w = NewWorld();
        var (a, b) = w.Trust.Edges().First();
        double before = w.Trust[a, b];
        double influence = w.Influence;
        var result = w.Apply(new Mediate(a, b));
        Assert.True(result.Ok, result.Message);
        Assert.True(w.Trust[a, b] > before);
        Assert.Equal(influence - w.Balance.CostMediate, w.Influence, 6);
    }

    [Fact]
    public void Actions_fail_cleanly_without_influence()
    {
        var w = NewWorld(balance: new Balance { StartInfluence = 0 });
        var result = w.Apply(new SponsorCongress());
        Assert.False(result.Ok);
        Assert.Equal(0, w.Influence);
    }

    [Fact]
    public void Holding_a_seceding_commune_costs_trust_and_drift()
    {
        var w = NewWorld();
        var c = w.Communes[5];
        w.PendingSecessions.Add(new PendingSecession(c.Id, w.Tick + 2));
        double trustBefore = w.Trust.AverageTrust(c.Id);
        var result = w.Apply(new HoldCommune(c.Id));
        Assert.True(result.Ok, result.Message);
        Assert.True(w.Trust.AverageTrust(c.Id) < trustBefore);
        w.Step();
        Assert.True(w.LastDrift.Events >= w.Balance.ForceStayDrift);
    }

    [Fact]
    public void Starving_commune_can_be_relieved_by_a_voluntary_project()
    {
        var w = NewWorld();
        var target = w.Communes.First(c => c.Kind == CommuneKind.Industrial);
        var result = w.Apply(new ProposeProject(ProjectKind.FamineRelief, target.Id));
        Assert.True(result.Ok, result.Message);
        var project = w.Projects.Single();
        Assert.DoesNotContain(target.Id, project.Invited);
        w.Run(30);
        Assert.NotEqual(ProjectStatus.Active, project.Status);
    }

    [Fact]
    public void Ideas_spread_through_trust()
    {
        var w = NewWorld();
        int Holders() => w.Communes.Count(c => c.Techs.Contains("libertarian_press"));
        int before = Holders();
        w.Run(60);
        Assert.True(Holders() > before);
    }

    [Fact]
    public void Balance_parameters_can_be_overridden_by_name()
    {
        var b = new Balance();
        b.Set("coordinationexponent", 1.7);
        b.Set("MaxActiveProjects", 5);
        Assert.Equal(1.7, b.CoordinationExponent);
        Assert.Equal(5, b.MaxActiveProjects);
        Assert.Throws<ArgumentException>(() => b.Set("NoSuchThing", 1));
    }

    [Theory]
    [InlineData("balanced", 40UL, 7)]
    [InlineData("centralizer", 11UL, 55)]
    [InlineData("educator", 5UL, 90)]
    public void Saved_game_continues_exactly_as_if_never_saved(string policyName, ulong seed, int saveAt)
    {
        var policy = Policies.Get(policyName);
        var straight = NewWorld(seed);
        var resumed = NewWorld(seed);
        for (int i = 0; i < saveAt; i++)
        {
            policy.Act(resumed);
            resumed.Step();
        }
        resumed = World.LoadJson(resumed.SaveJson(), resumed.Techs);

        foreach (var w in new[] { straight, resumed })
            while (w.Outcome == Outcome.Ongoing)
            {
                policy.Act(w);
                w.Step();
            }

        Assert.Equal(straight.Log.Select(e => e.Text), resumed.Log.Select(e => e.Text));
        Assert.Equal(straight.Scores.Total, resumed.Scores.Total);
        Assert.Equal(straight.Drift.Value, resumed.Drift.Value);
        Assert.Equal(straight.Rng.State, resumed.Rng.State);
        Assert.Equal(straight.History, resumed.History);
    }

    [Fact]
    public void Corrupt_or_foreign_saves_are_rejected()
    {
        var techs = NewWorld().Techs;
        Assert.Throws<InvalidDataException>(() => World.LoadJson("""{ "version": 99 }""", techs));
        Assert.ThrowsAny<Exception>(() => World.LoadJson("not json", techs));
    }

    [Fact]
    public void Historical_event_fires_and_its_choice_applies()
    {
        var w = NewWorld();
        w.Run(11);
        var pending = Assert.Single(w.PendingEvents);
        Assert.Equal("cnt_joins_government", pending.Id);
        double influence = w.Influence;
        var result = w.Apply(new ChooseEventOption(pending.Id, 1)); // stay outside
        Assert.True(result.Ok, result.Message);
        Assert.Empty(w.PendingEvents);
        Assert.Equal(influence - 5, w.Influence, 6);
    }

    [Fact]
    public void Unanswered_event_takes_its_default_option()
    {
        var w = NewWorld();
        w.Run(11);
        Assert.Single(w.PendingEvents);
        w.Run(5);
        Assert.DoesNotContain(w.PendingEvents, p => p.Id == "cnt_joins_government");
        Assert.Contains(w.Log, e => e.Text.StartsWith("Into the Generalitat?: with no answer"));
    }

    /// <summary>
    /// The core design claim, as a regression test: centralizing everything wins the war but
    /// loses the revolution, and doing nothing is worse at survival than playing the dilemma.
    /// </summary>
    [Fact]
    public void Design_dilemma_holds_across_seeds()
    {
        var seeds = Enumerable.Range(1, 20).Select(i => (ulong)i).ToList();
        var central = seeds.Select(s => Play(new Centralizer(), s)).ToList();
        var passive = seeds.Select(s => Play(new Passive(), s)).ToList();
        var balanced = seeds.Select(s => Play(new Balanced(), s)).ToList();

        Assert.True(central.Count(w => w.Outcome == Outcome.BecameState) >= 15, "centralizing should usually create a state");
        Assert.True(balanced.Average(w => w.Scores.Survival) > passive.Average(w => w.Scores.Survival));
        Assert.True(balanced.Average(w => w.Scores.Total) > central.Average(w => w.Scores.Total));
    }
}
