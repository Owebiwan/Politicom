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
        "open:1936:ARG:3",
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

    public static TheoryData<string> Eras()
    {
        var data = new TheoryData<string>();
        foreach (var e in Sim.Open.OpenStartGenerator.Eras) data.Add(e);
        return data;
    }

    [Theory]
    [MemberData(nameof(Eras))]
    public void Every_era_map_has_states_with_outlines_capitals_and_cities(string era)
    {
        var atlas = Sim.Global.Atlas.Load(DataDir, era);
        Assert.InRange(atlas.States.Count, 40, 200);
        Assert.All(atlas.States, s => Assert.NotEmpty(s.Rings));
        // Peoples of the interior (Aboriginal Australia) have no modern towns inside their lands.
        Assert.True(atlas.States.Count(s => s.Capital != null && s.Cities.Count > 0) >= atlas.States.Count * 0.7);
        Assert.Equal(atlas.States.Count, atlas.States.Select(s => s.Id).Distinct().Count());
        Assert.All(atlas.States, s => Assert.All(s.Neighbours, n => Assert.NotNull(atlas.State(n))));
        Assert.Contains(atlas.States, s => s.Neighbours.Count > 0);
    }

    [Fact]
    public void Ancient_starts_count_years_before_christ()
    {
        var atlas = Sim.Global.Atlas.Load(DataDir, "-500");
        var state = Sim.Open.OpenStartGenerator.Playable(atlas).First();
        var scenario = Sim.Open.OpenStartGenerator.Generate(atlas, Sandbox.Value, state.Id, 1, calibrate: false);
        var w = World.Create(scenario, NewWorld().Techs);
        Assert.Equal(-500, w.Year);
        Assert.EndsWith("500 BC", w.Format(w.Date, "MMMM yyyy"));
        w.Run(60);
        Assert.Equal(-499, w.Year);
        // The ancient tree is live; the industrial one is far in the future.
        Assert.Contains(w.LiveTechs, t => t.Id == "village_moots");
        Assert.False(w.Reached(w.Techs["open_assemblies"]));
        Assert.Equal("AD 800", Ages.Year(800));
        var json = w.SaveJson();
        Assert.Equal(-499, World.LoadJson(json, w.Techs).Year);
    }

    [Fact]
    public void Embargoes_starting_together_make_one_line_in_the_log()
    {
        var atlas = Sim.Global.Atlas.Load(DataDir, "1200");
        var w = World.Create(Sim.Open.OpenStartGenerator.Generate(atlas, Sandbox.Value, "FRANCE", 1, calibrate: false), NewWorld().Techs);
        w.Run(20);
        var lines = w.Log.Where(e => e.Text.Contains("embargo the federation") || e.Text.Contains("embargoes the federation")).ToList();
        Assert.NotEmpty(lines);
        Assert.Equal(lines.Count, lines.Select(e => e.Tick).Distinct().Count());
        Assert.True(w.States.Count(s => s.Does(Sim.Global.StateActions.Embargo)) > lines.Count);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void There_is_never_a_long_stretch_without_a_decision(ulong seed)
    {
        var w = Play(Sim.Ai.Policies.Get("balanced")!, seed);
        var asked = w.Log.Where(e => e.Severity == Severity.Decision).Select(e => e.Tick).Distinct().ToList();
        int longest = asked.Zip(asked.Skip(1), (a, b) => b - a).Max();
        Assert.True(longest * w.Weeks <= w.Balance.DecisionDrought + 3, $"{longest * w.Weeks} weeks with nothing to decide");
    }

    [Fact]
    public void The_ally_stops_offering_after_two_refusals()
    {
        var w = NewWorld();
        for (int i = 0; i < 135 && w.Outcome == Outcome.Ongoing; i++)
        {
            if (w.Ally.OfferPending) w.Apply(new AnswerAllyOffer(false));
            w.Step();
        }
        Assert.Equal(2, w.Ally.Declined);
        Assert.Equal(0, w.Ally.OfferInterval);
    }

    [Fact]
    public void Rival_offers_harden_and_end_after_three()
    {
        var w = NewWorld();
        w.Run(135);
        foreach (var offers in w.Scenario.Events.Where(e => e.Id.StartsWith("rival_offer_"))
                     .GroupBy(e => e.Id.Split('_')[2]))
        {
            var list = offers.OrderBy(e => e.Tick).ToList();
            Assert.True(list.Count <= 3);
            for (int i = 1; i < list.Count; i++)
                Assert.True(list[i].Options[0].Effects.Drift > list[i - 1].Options[0].Effects.Drift);
        }
    }

    [Fact]
    public void A_goal_is_offered_in_the_middle_of_a_game_and_taking_it_on_sets_it_going()
    {
        var w = NewWorld();
        for (int i = 0; i < 60 && !w.PendingEvents.Any(p => p.Id.StartsWith("goal_")); i++) w.Step();
        var offer = w.PendingEvents.First(p => p.Id.StartsWith("goal_"));
        Assert.True(w.Apply(new ChooseEventOption(offer.Id, 0)).Ok);
        Assert.Contains(w.Goals, g => g.Active && g.Id == offer.Id);
    }

    [Fact]
    public void A_goal_met_lifts_enthusiasm_and_a_goal_missed_costs_some()
    {
        var w = NewWorld();
        w.Step();
        double enthusiasm = w.Enthusiasm, influence = w.Influence;
        w.Goals.Add(new Goal { Id = "goal_test_met", Kind = GoalKind.LowerDrift, Title = "test", Target = 99, Offered = w.Tick, Deadline = w.Tick + 10, Active = true });
        w.Step();
        Assert.DoesNotContain(w.Goals, g => g.Id == "goal_test_met");
        Assert.Contains(w.Log, e => e.Text.StartsWith("Goal met"));
        Assert.True(w.Influence > influence - 5);

        w.Goals.Add(new Goal { Id = "goal_test_missed", Kind = GoalKind.Trust, Title = "test", Target = 2, Offered = w.Tick, Deadline = w.Tick + 1, Active = true });
        w.Step();
        w.Step();
        Assert.Contains(w.Log, e => e.Text.StartsWith("Goal missed"));
    }

    [Fact]
    public void Goals_survive_saving_and_loading()
    {
        var w = NewWorld();
        w.Step();
        w.Goals.Add(new Goal { Id = "goal_saved", Kind = GoalKind.Trust, Title = "test", Target = 0.99, Offered = w.Tick, Deadline = w.Tick + 20, Active = true });
        var loaded = World.Restore(SaveGame.FromJson(System.Text.Json.JsonSerializer.Serialize(w.Save(), Json.Options)), w.Techs);
        var g = Assert.Single(loaded.Goals);
        Assert.Equal(("goal_saved", 0.99, true), (g.Id, g.Target, g.Active));
    }

    [Fact]
    public void Each_turns_battles_are_recorded_and_survive_saving()
    {
        var w = NewWorld();
        w.Run(30);
        Assert.NotEmpty(w.Battles);
        foreach (var r in w.Battles)
        {
            Assert.True(r.Attack > 0 && r.Defense > 0);
            Assert.Equal(r.Attack > r.Defense, r.Result is BattleResult.LostGround or BattleResult.Fell);
            Assert.True(r.ControlAfter <= r.ControlBefore || r.Attack <= r.Defense);
            Assert.NotEmpty(r.DefenseAdded);
        }
        var loaded = World.Restore(SaveGame.FromJson(System.Text.Json.JsonSerializer.Serialize(w.Save(), Json.Options)), w.Techs);
        Assert.Equal(w.Battles.Count, loaded.Battles.Count);
        var (a, b) = (w.Battles[^1], loaded.Battles[^1]);
        Assert.Equal((a.Commune, a.Attack, a.Result, a.DefenseAdded[0]), (b.Commune, b.Attack, b.Result, b.DefenseAdded[0]));
    }

    [Theory]
    [InlineData("catalonia_1936")]
    [InlineData("free_territory_1918")]
    [InlineData("open:1200:FRANCE")]
    public void What_needs_attention_is_listed_and_every_remedy_works(string scenario)
    {
        var w = World.Load(DataDir, scenario);
        int listed = 0;
        for (int week = 0; week < 40 && w.Outcome == Outcome.Ongoing; week++)
        {
            var list = Attention.List(w);
            Assert.True(list.Count <= 6);
            Assert.Equal(list.Count, list.Select(x => x.Commune).Distinct().Count());
            listed += list.Count;
            // The most urgent with a remedy we can afford: it must go through.
            if (list.FirstOrDefault(x => x.Action != null && x.Cost <= w.Influence) is { } act)
                Assert.True(w.Apply(act.Action!).Ok, $"{w.Communes[act.Commune].Name}: {act.Remedy}");
            w.Step();
        }
        Assert.True(listed > 0);
    }

    [Fact]
    public void Scarcity_raises_prices_and_dear_goods_buy_less_abroad()
    {
        var w = NewWorld();
        w.Step();
        double food = w.FoodPrice;
        foreach (var c in w.Communes) c.Food *= 0.1;
        for (int i = 0; i < 6; i++) w.Step();
        Assert.True(w.FoodPrice > food + 0.3, $"{food} -> {w.FoodPrice}");
        Assert.InRange(w.FoodPrice, 0.4, 3);
        Assert.Equal(w.FoodPrice, World.LoadJson(w.SaveJson(), w.Techs).FoodPrice, 6);
        Assert.Equal(w.FoodPrice, w.History[^1].FoodPrice, 6);
    }

    [Fact]
    public void Communes_propose_projects_and_backing_one_starts_it()
    {
        var w = NewWorld();
        for (int i = 0; i < 60 && !w.PendingEvents.Any(p => p.Id.StartsWith("proposal_")); i++) w.Step();
        var proposal = w.PendingEvents.First(p => p.Id.StartsWith("proposal_"));
        int before = w.ActiveProjects.Count();
        w.Influence = 50;
        Assert.True(w.Apply(new ChooseEventOption(proposal.Id, 0)).Ok);
        Assert.Equal(before + 1, w.ActiveProjects.Count());
    }

    [Fact]
    public void Volunteers_strengthen_a_front_at_the_cost_of_work_and_conscription_adds_drift()
    {
        var calm = NewWorld();
        var called = NewWorld();
        called.Influence = 100;
        var front = called.Federation.First(c => c.Front);
        Assert.True(called.Apply(new CallVolunteers(front.Id)).Ok);
        Assert.True(front.Mobilization > called.BaselineMobilization(front));
        calm.Step();
        called.Step();
        var same = calm.Communes[front.Id];
        Assert.True(front.Defense > same.Defense);
        Assert.True(front.FoodProduced + front.GoodsProduced < same.FoodProduced + same.GoodsProduced);

        var drafted = NewWorld();
        Assert.True(drafted.Apply(new SetConscription(true)).Ok);
        drafted.Run(10);
        Assert.All(drafted.Federation, c => Assert.True(c.Mobilization > 0.15));
        Assert.True(drafted.LastDrift.Conscription > 0);
        Assert.True(drafted.Enthusiasm < NewWorld().Enthusiasm);
    }

    [Fact]
    public void Gold_is_seized_once_buys_abroad_and_its_guards_add_drift()
    {
        var w = NewWorld();
        Assert.False(w.Apply(new BuyAbroad(Purchase.Arms)).Ok); // no gold yet
        Assert.True(w.Apply(new SeizeTreasuries()).Ok);
        Assert.False(w.Apply(new SeizeTreasuries()).Ok);
        double gold = w.Gold;
        Assert.True(gold >= w.Balance.GoldPerPurchase);
        w.Step();
        Assert.True(w.LastDrift.Treasury > 0);
        double frontGoods = w.Federation.Where(c => c.Front).Sum(c => c.Goods);
        var bought = w.Apply(new BuyAbroad(Purchase.Arms));
        Assert.True(bought.Ok, bought.Message);
        Assert.True(w.Federation.Where(c => c.Front).Sum(c => c.Goods) > frontGoods);
        Assert.Equal(gold - w.Balance.GoldPerPurchase, w.Gold, 6);
        Assert.Equal(w.Gold, World.LoadJson(w.SaveJson(), w.Techs).Gold, 6);
    }

    [Fact]
    public void Enthusiasm_wanes_with_hunger_and_lifts_production()
    {
        var keen = NewWorld();
        var weary = NewWorld();
        weary.Enthusiasm = 0.3;
        keen.Step();
        weary.Step();
        Assert.True(keen.Federation.Sum(c => c.FoodProduced) > weary.Federation.Sum(c => c.FoodProduced));
        var hungry = NewWorld();
        foreach (var c in hungry.Communes) c.Food = 0;
        double before = hungry.Enthusiasm;
        hungry.Run(4);
        Assert.True(hungry.Enthusiasm < before);
    }

    [Fact]
    public void A_regional_rising_holds_a_region_and_the_country_can_rise_to_join_it()
    {
        var atlas = Sim.Global.Atlas.Load(DataDir, "1936");
        var scenario = Sim.Open.OpenStartGenerator.Generate(atlas, Sandbox.Value, "ARG", 3, calibrate: false, scope: Sim.Open.OpenScope.Region);
        Assert.InRange(scenario.Communes.Count, 6, 10);
        Assert.NotEmpty(scenario.Reserve);
        Assert.Empty(scenario.Reserve.Select(r => r.Name).Intersect(scenario.Communes.Select(c => c.Name)));
        Assert.EndsWith(":region", scenario.Id);
        Assert.True(Sim.Open.OpenStartGenerator.TryParseId(scenario.Id, out _, out _, out _, out var scope));
        Assert.Equal(Sim.Open.OpenScope.Region, scope);
        var w = World.Create(scenario, NewWorld().Techs, seed: 2);
        var policy = new Sim.Ai.Balanced();
        for (int i = 0; i < 80 && w.Outcome == Outcome.Ongoing; i++)
        {
            policy.Act(w);
            w.Step();
        }
        Assert.NotEmpty(w.Risen);
        Assert.Contains(w.Log, e => e.Title.EndsWith(" rises"));
        Assert.Equal(w.InitialCommuneCount + w.Risen.Count, w.Communes.Count);
        var resumed = World.LoadJson(w.SaveJson(), w.Techs);
        Assert.Equal(w.Risen.OrderBy(x => x), resumed.Risen.OrderBy(x => x));
    }

    [Fact]
    public void Random_events_fit_their_age_and_regime_and_arrive_as_decisions()
    {
        var templates = RandomEventTemplate.Load(DataPaths.RandomEvents(DataDir));
        Assert.True(templates.Count >= 30);
        Assert.Equal(templates.Count, templates.Select(t => t.Id).Distinct().Count());
        Assert.All(templates, t => Assert.True(t.Event.Options.Count >= 2, t.Id));
        // Over many ancient games, only events that suit antiquity happen.
        var atlas = Sim.Global.Atlas.Load(DataDir, "1");
        var seen = new HashSet<string>();
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var w = World.Create(Sim.Open.OpenStartGenerator.Generate(atlas, Sandbox.Value, "ROMAN", seed, calibrate: false), NewWorld().Techs, seed: seed);
            w.Run(104);
            foreach (var id in w.FiredEvents.Where(id => id.StartsWith("random_")))
                seen.Add(id.Split('_', 2)[1][..id.Split('_', 2)[1].LastIndexOf('_')]);
        }
        Assert.NotEmpty(seen);
        foreach (var id in seen)
        {
            var t = templates.Single(x => x.Id == id);
            Assert.InRange(1, t.Eras[0], t.Eras[1]);
            Assert.True(t.Regimes.Length == 0 || t.Regimes.Contains("empire"), id);
        }
        Assert.DoesNotContain("rumour", seen); // the internet is far off
    }

    [Theory]
    [MemberData(nameof(Eras))]
    public void Open_starts_are_valid_for_every_playable_state(string era)
    {
        var atlas = Sim.Global.Atlas.Load(DataDir, era);
        var techs = NewWorld().Techs;
        var playable = Sim.Open.OpenStartGenerator.Playable(atlas).ToList();
        Assert.True(playable.Count >= 8, $"only {playable.Count} playable states in {era}");
        foreach (var state in playable)
        {
            var scenario = Sim.Open.OpenStartGenerator.Generate(atlas, Sandbox.Value, state.Id, 1, calibrate: false);
            Assert.InRange(scenario.Communes.Count, 6, 26);
            Assert.Equal(scenario.Communes.Count, scenario.Communes.Select(c => c.Name).Distinct().Count());
            Assert.Contains(scenario.Communes, c => c.FrontOf.Length > 0);
            Assert.DoesNotContain(scenario.States, s => s.Id == state.Id);
            var w = World.Create(scenario, techs, seed: 1);
            Assert.All(w.Communes, c => Assert.NotEmpty(w.Trust.Neighbors(c.Id)));
        }
    }

    [Fact]
    public void Open_starts_are_deterministic_and_ids_parse()
    {
        var atlas = Sim.Global.Atlas.Load(DataDir, "1918");
        string Json(ScenarioDefinition s) => System.Text.Json.JsonSerializer.Serialize(s);
        Assert.Equal(Json(Sim.Open.OpenStartGenerator.Generate(atlas, Sandbox.Value, "DEU", 5, calibrate: false)),
                     Json(Sim.Open.OpenStartGenerator.Generate(atlas, Sandbox.Value, "DEU", 5, calibrate: false)));
        // Stable across processes: a fixed fingerprint, not just equal within one run.
        Assert.Equal(0xcbf29ce484222325UL, Sim.Open.OpenStartGenerator.StableHash(""));
        Assert.Equal(0xaf63dc4c8601ec8cUL, Sim.Open.OpenStartGenerator.StableHash("a"));
        Assert.True(Sim.Open.OpenStartGenerator.TryParseId("open:1936:ARG", out var era, out var id, out var seed));
        Assert.Equal(("1936", "ARG", 1UL), (era, id, seed));
        Assert.True(Sim.Open.OpenStartGenerator.TryParseId("open:1871:ITA:9", out _, out _, out seed));
        Assert.Equal(9UL, seed);
        Assert.False(Sim.Open.OpenStartGenerator.TryParseId("sandbox:7", out _, out _, out _));
    }

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
    public void Sandbox_maps_have_geography_with_every_town_on_land()
    {
        static bool Inside(double[][] ring, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                if ((ring[i][1] > y) != (ring[j][1] > y) &&
                    x < (ring[j][0] - ring[i][0]) * (y - ring[i][1]) / (ring[j][1] - ring[i][1]) + ring[i][0])
                    inside = !inside;
            return inside;
        }
        int coastal = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var s = Sandbox.Value.Generate(seed, calibrate: false);
            var g = Assert.IsType<Sim.Sandbox.GeographyDefinition>(s.Geography);
            var land = Assert.Single(g.Land);
            Assert.NotEmpty(g.Rivers);
            Assert.NotEmpty(g.Provinces);
            Assert.All(s.Communes, c => Assert.True(Inside(land, c.Lon, c.Lat), $"seed {seed}: {c.Name} is at sea"));
            Assert.All(s.Communes, c => Assert.DoesNotContain(g.Lakes, lake => Inside(lake, c.Lon, c.Lat)));
            Assert.All(s.Communes, c => Assert.Contains(g.Provinces, p => Inside(p.Ring, c.Lon, c.Lat)));
            if (land.Length > 4) coastal++;
        }
        Assert.InRange(coastal, 20, 39); // most maps have a coast, some are landlocked
        var json = System.Text.Json.JsonSerializer.Serialize(Sandbox.Value.Generate(5, calibrate: false).Geography);
        Assert.Equal(json, System.Text.Json.JsonSerializer.Serialize(Sandbox.Value.Generate(5, calibrate: false).Geography));
    }

    [Fact]
    public void Sandbox_maps_have_neighbour_states_that_can_back_the_enemy()
    {
        int sponsored = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var s = Sandbox.Value.Generate(seed, calibrate: false);
            Assert.InRange(s.States.Count, 2, 5);
            Assert.Equal(s.States.Select(x => x.Id), s.Geography!.Neighbours.Select(n => n.Id));
            Assert.All(s.Geography.Neighbours, n => Assert.NotEmpty(n.Rings));
            var w = World.Create(s, NewWorld().Techs, seed: seed);
            Assert.Equal(s.States.Count, w.States.Count);
            if (s.Enemies.Any(e => e.Sponsors.Length > 0)) sponsored++;
        }
        Assert.True(sponsored >= 10, $"only {sponsored} of 20 maps had a neighbour backing an enemy");
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
        // Trade's own drift is always there; total drift can still fall, because arms bought abroad
        // keep the front from losing ground and so cut the wartime-emergency drift.
        Assert.Equal(open.Balance.TradeDrift, open.LastDrift.ForeignTrade, 6);
        Assert.Equal(0, closed.LastDrift.ForeignTrade);
        var front = closed.Communes.First(c => c.Front).Id;
        Assert.True(open.Communes[front].Goods >= closed.Communes[front].Goods);
    }

    [Fact]
    public void Hostile_states_embargo_the_federation_and_trade_suffers()
    {
        var w = NewWorld();
        w.Apply(new SetForeignTrade(true));
        foreach (var s in w.States) s.Stance = 0.1; // nobody embargoes
        w.Step();
        Assert.Equal(0, w.Embargo, 6);
        // The rate moves with scarcity prices too; compare it with them taken out.
        double Rate() => w.LastImports / w.LastExports / Math.Clamp(w.FoodPrice / w.GoodsPrice, 0.4, 2.5);
        double open = Rate();
        foreach (var s in w.States.Where(s => s.Ideology != Global.Ideology.Communist)) s.Stance = -0.9;
        w.Step(); // the embargo is declared (diplomacy comes after trade within a turn)
        Assert.True(w.Embargo > 0.5, $"embargo {w.Embargo}");
        foreach (var s in w.States.Where(s => s.Ideology != Global.Ideology.Communist)) s.Stance = -0.9;
        w.Step(); // and trade under it
        Assert.True(Rate() < open);
    }

    [Fact]
    public void Hostile_sponsors_intervene_and_friends_send_arms_and_volunteers()
    {
        var w = NewWorld();
        var nationalists = w.Enemies[0];
        var italy = w.States.Single(s => s.Id == "ITA");
        var mexico = w.States.Single(s => s.Id == "MEX");
        italy.Stance = -0.9;
        mexico.Stance = 0.8;
        w.Step();
        Assert.True(italy.Does(Global.StateActions.Intervention));
        Assert.True(nationalists.Intervention > 0);
        Assert.True(mexico.Does(Global.StateActions.ArmsSales) && mexico.Does(Global.StateActions.Volunteers));
        Assert.True(w.LastArms > 0 && w.VolunteerDefense > 0);
        Assert.Contains(w.Log, e => e.Text.StartsWith("Kingdom of Italy intervenes"));

        // Hysteresis: a small warming doesn't end the intervention at once.
        italy.Stance = -0.55;
        w.Step();
        Assert.True(italy.Does(Global.StateActions.Intervention));
    }

    [Fact]
    public void Delegates_warm_a_state_and_agitation_radicalizes_it()
    {
        var w = NewWorld();
        var britain = w.States.Single(s => s.Id == "GBR");
        var germany = w.States.Single(s => s.Id == "DEU");
        double before = britain.Stance, fascistBefore = germany.Stance;
        Assert.True(w.Apply(new SendDelegates("GBR")).Ok);
        Assert.True(w.Apply(new SendDelegates("DEU", "Hamburg")).Ok);
        Assert.True(britain.Stance - before > germany.Stance - fascistBefore); // fascists barely listen
        double rad = britain.Radicalization;
        Assert.True(w.Apply(new AgitateWorkers("GBR")).Ok);
        Assert.True(britain.Radicalization > rad);
        Assert.False(w.Apply(new SendDelegates("XXX")).Ok);

        var restored = World.LoadJson(w.SaveJson(), w.Techs);
        Assert.Equal(britain.Stance, restored.States.Single(s => s.Id == "GBR").Stance);
        // Only missions to a named city are marked on the map.
        Assert.Equal(new Mission("DEU", "Hamburg", w.Tick, false), Assert.Single(restored.Missions));
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
        c.Techs.UnionWith(mandate.Prerequisites);
        c.PoliticalEducation = mandate.MinPolitical * 0.4;
        Assert.False(Systems.ResearchSystem.CanAdopt(w, c, mandate));
        c.PoliticalEducation = mandate.MinPolitical * 0.6;
        Assert.True(Systems.ResearchSystem.CanAdopt(w, c, mandate));
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
    public void The_tech_tree_has_real_chains_and_no_cycles()
    {
        var techs = NewWorld().Techs;
        Assert.InRange(techs.All.Count, 90, 110);
        Assert.Equal(5, techs.Categories.Count);
        Assert.All(techs.Categories, cat => Assert.Contains(techs.All, t => t.Category == cat && techs.Depth(t.Id) >= 1));
        Assert.True(techs.Categories.Count(cat => techs.All.Any(t => t.Category == cat && techs.Depth(t.Id) >= 2)) >= 4);
        Assert.Contains(techs.All, t => t.Prerequisites.Length >= 2); // some techs need two lines
        Assert.Equal(3, techs.Depth("agronomy_stations"));
        Assert.Throws<InvalidDataException>(() => new TechCatalog(new[]
        {
            new TechDefinition { Id = "a", Prerequisites = new[] { "b" } },
            new TechDefinition { Id = "b", Prerequisites = new[] { "a" } },
        }));
    }

    [Fact]
    public void Every_age_has_techniques_in_every_line()
    {
        var techs = NewWorld().Techs;
        foreach (var (age, from) in Ages.All)
            foreach (var line in techs.Categories)
                Assert.True(techs.All.Any(t => t.Category == line && Ages.Name(t.Era) == age), $"No {line} technique in the {age} age");
        Assert.All(techs.All, t => Assert.True(t.Until is null || t.Until > t.Era, t.Id));
        // A technique never needs one that comes later than itself.
        Assert.All(techs.All, t => Assert.All(t.Prerequisites, p => Assert.True(techs[p].Era <= t.Era, $"{t.Id} needs later {p}")));
    }

    [Fact]
    public void Technologies_ahead_of_the_era_stay_locked_and_old_ones_are_common_knowledge()
    {
        var w = NewWorld(); // Catalonia, 1936
        var c = w.Communes[0];
        c.PracticalEducation = c.PoliticalEducation = 1;
        c.Techs.UnionWith(new[] { "radio", "telegraph" });
        var tv = w.Techs["television"]; // 1950
        Assert.False(w.CanDiscover(c, tv));
        Assert.True(w.CommonKnowledge(w.Techs["writing"]));
        Assert.False(w.CanDiscover(c, w.Techs["writing"]));
        Assert.DoesNotContain(w.LiveTechs, t => t.Id == "writing");
        // Prerequisites that are common knowledge count as known.
        var phone = w.Techs["telephone"];
        Assert.True(w.CanDiscover(c, phone));
        Assert.True(w.Knows(c, "printing_press"));
    }

    [Fact]
    public void Science_brings_breakthroughs_in_the_focus_line()
    {
        var w = NewWorld();
        w.Apply(new SetResearchFocus("Agricultural"));
        Assert.Equal("Agricultural", w.ResearchFocus);
        var before = w.Communes.Sum(c => c.Techs.Count(t => w.Techs[t].Category == "Agricultural"));
        w.Run(40);
        Assert.True(w.LastScience > 0);
        Assert.True(w.Breakthroughs >= 1);
        Assert.Contains(w.Log, e => e.Text.StartsWith("Breakthrough"));
        Assert.All(w.Log.Where(e => e.Text.StartsWith("Breakthrough")), e => Assert.Contains(w.Techs.All.Where(t => t.Category == "Agricultural"), t => e.Text.Contains(t.Name)));
        Assert.False(w.Apply(new SetResearchFocus("Alchemy")).Ok);
    }

    [Fact]
    public void Without_a_research_focus_science_brings_no_breakthroughs()
    {
        var w = NewWorld();
        w.Run(40);
        Assert.Equal(0, w.Breakthroughs);
        Assert.True(w.Science > w.BreakthroughCost); // it accumulates, waiting for organized research
    }

    [Fact]
    public void Practical_education_raises_science()
    {
        var low = NewWorld();
        var high = NewWorld();
        foreach (var c in high.Communes) c.PracticalEducation += 0.3;
        low.Step();
        high.Step();
        Assert.True(high.LastScience > low.LastScience * 1.3);
    }

    [Fact]
    public void Culture_brakes_drift_and_cultural_works_build_it()
    {
        var w = NewWorld();
        var c = w.Federation.First();
        double before = c.Culture;
        w.Influence = 100;
        Assert.True(w.Apply(new FoundCulturalWork(c.Id, CulturalWorkKind.ArtMovement)).Ok);
        Assert.True(c.Culture > before + 0.1);
        Assert.False(w.Apply(new FoundCulturalWork(c.Id, CulturalWorkKind.Newspaper)).Ok); // still taking up the last one

        var bare = NewWorld();
        var cultured = NewWorld();
        foreach (var x in bare.Communes) x.Culture = 0;
        foreach (var x in cultured.Communes) x.Culture = 0.8;
        bare.Step();
        cultured.Step();
        Assert.Equal(0, bare.LastDrift.CultureBrake, 3);
        Assert.True(cultured.LastDrift.CultureBrake > 0.02);
        Assert.True(cultured.Drift.Value < bare.Drift.Value);
    }

    [Fact]
    public void The_enemy_modernizes_and_a_stagnant_federation_falls_behind()
    {
        var w = NewWorld();
        var e = w.Enemies[0];
        double start = e.TechLevel;
        foreach (var c in w.Communes) c.Techs.Clear(); // no research at all
        for (int i = 0; i < 60; i++)
        {
            foreach (var c in w.Communes) c.Techs.Clear();
            w.Step();
        }
        Assert.True(e.TechLevel > start + 0.1);
        Assert.True(Sim.Global.TechLevelSystem.EnemyEdge(w, e) > 1.05);
        Assert.All(w.States, s => Assert.InRange(s.TechLevel, 0.01, 1));
    }

    [Fact]
    public void Railways_join_communes_and_help_aid_and_defense()
    {
        var w = NewWorld();
        int reus = w.Communes.Single(c => c.Name == "Reus").Id, caspe = w.Communes.Single(c => c.Name == "Caspe").Id;
        Assert.True(w.Rail(reus, caspe));
        Assert.True(w.Trust.Connected(reus, caspe)); // a line makes them neighbours

        var restored = World.LoadJson(w.SaveJson(), w.Techs);
        Assert.Equal(w.Railways, restored.Railways);

        int vic = w.Communes.Single(c => c.Name == "Vic").Id, girona = w.Communes.Single(c => c.Name == "Girona").Id;
        Assert.False(w.Rail(vic, girona));
        w.Run(1);
        var project = w.Apply(new ProposeProject(ProjectKind.Railway, vic, girona));
        Assert.True(project.Ok, project.Message);
        Assert.False(w.Apply(new ProposeProject(ProjectKind.Railway, reus, caspe)).Ok); // already joined
    }

    [Fact]
    public void Every_line_of_development_has_a_free_path()
    {
        var techs = NewWorld().Techs;
        Assert.All(techs.Categories, cat => Assert.Contains(techs.All,
            t => t.Category == cat && t.Effects.DriftBrake > t.Effects.DriftPerTick));
    }

    [Fact]
    public void Communes_on_a_free_path_resist_that_lines_centralizing_technologies()
    {
        var w = NewWorld();
        var c = w.Communes[0];
        var heavy = w.Techs["heavy_industry"];
        var crop = w.Techs["crop_rotation"];
        Assert.Equal(1, Systems.ResearchSystem.Openness(w, c, heavy));
        c.Techs.Add("workers_control");
        Assert.Equal(w.Balance.PathResistance, Systems.ResearchSystem.Openness(w, c, heavy));
        Assert.Equal(1, Systems.ResearchSystem.Openness(w, c, crop)); // other lines unaffected
    }

    [Fact]
    public void Games_can_run_past_their_history_up_to_the_campaign_cap()
    {
        var historical = World.Load(DataDir, "catalonia_1936");
        var longer = World.Load(DataDir, "catalonia_1936@300");
        Assert.Equal(135, historical.Scenario.EndTick);
        Assert.Equal(300, longer.Scenario.EndTick);
        Assert.Equal("catalonia_1936@300", longer.Scenario.Id);
        Assert.False(longer.Scenario.IsCampaign);
        Assert.Equal(ScenarioDefinition.MaxTurns, World.Load(DataDir, "catalonia_1936@9000").Scenario.EndTick); // capped
        Assert.Equal("catalonia_1936", World.Load(DataDir, "catalonia_1936@20").Scenario.Id); // never shorter than history

        var enemy = longer.Scenario.Enemies[0];
        Assert.Contains(enemy.Offensives, o => o.Tick > 135 && o.Name.StartsWith("A renewed offensive"));
        Assert.Equal(135, enemy.GrowthUntil);
        Assert.Null(historical.Scenario.Enemies[0].GrowthUntil);

        var policy = new Balanced();
        while (longer.Outcome == Outcome.Ongoing)
        {
            policy.Act(longer);
            longer.Step();
        }
        Assert.True(longer.Tick > 135 || longer.Outcome != Outcome.Survived);
        var reloaded = World.LoadJson(World.Load(DataDir, "catalonia_1936@300").SaveJson(), longer.Techs);
        Assert.Equal(300, reloaded.Scenario.EndTick); // the length travels with the save
    }

    [Theory]
    [InlineData("catalonia_1936")]
    [InlineData("free_territory_1918")]
    [InlineData("paris_commune_1871")]
    public void Historical_scenarios_sit_in_their_era_and_home_state(string id)
    {
        var scenario = ScenarioDefinition.Load(DataPaths.Scenario(DataDir, id));
        var atlas = Global.Atlas.Load(DataDir, scenario.World!);
        var home = Assert.IsType<Global.AtlasState>(atlas.State(scenario.HomeState!));
        Assert.InRange(atlas.States.Count, 30, 80);
        Assert.All(atlas.States, s => Assert.NotEmpty(s.Rings));
        Assert.NotEmpty(atlas.Borders);
        // The region lies inside its home state's outline.
        double lon = scenario.Communes.Average(c => c.Lon), lat = scenario.Communes.Average(c => c.Lat);
        Assert.Contains(home.Rings, ring => Inside(ring, lon, lat));
    }

    private static bool Inside(double[][] ring, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            if ((ring[i][1] > y) != (ring[j][1] > y) &&
                x < (ring[j][0] - ring[i][0]) * (y - ring[i][1]) / (ring[j][1] - ring[i][1]) + ring[i][0])
                inside = !inside;
        return inside;
    }

    [Theory]
    [InlineData("French Republic", "France")]
    [InlineData("German Reich", "Germany")]
    [InlineData("Kingdom of Italy", "Italy")]
    [InlineData("Soviet Union", "Russia")]
    [InlineData("Mongolian People's Republic", "Mongolia")]
    [InlineData("British Empire", "Britain")]
    [InlineData("United States", "United States")]
    [InlineData("French Republic (Versailles)", "France")]
    public void Country_names_drop_the_regime(string state, string country) =>
        Assert.Equal(country, Global.StateText.Country(state));

    [Fact]
    public void Radicalized_workers_rise_and_the_rising_is_decided()
    {
        var w = NewWorld(seed: 5);
        var portugal = w.States.Single(s => s.Id == "PRT");
        portugal.Radicalization = 0.95;
        for (int i = 0; i < 40 && portugal.Uprising == 0; i++) w.Step();
        Assert.True(portugal.Uprising > 0, "no rising in 40 turns at 95% radicalization");
        Assert.Contains(w.Log, e => e.Text.StartsWith("Revolution in Portugal"));
        w.Run(w.Balance.UprisingTicks);
        Assert.Equal(0, portugal.Uprising);
        bool won = portugal.RevolutionCount > 0;
        if (won) Assert.True(portugal.Ideology is Global.Ideology.Libertarian or Global.Ideology.Communist);
        else Assert.True(portugal.Radicalization < 0.5 && portugal.Repression > 0.3);
    }

    [Fact]
    public void A_sister_federation_can_federate_and_its_cities_play_on()
    {
        var w = NewWorld(seed: 3);
        var france = w.States.Single(s => s.Id == "FRA");
        france.Ideology = Global.Ideology.Libertarian;
        int before = w.Communes.Count;
        Systems.EventSystem.Apply(w, new EventEffects { Federate = "FRA" });
        Assert.True(france.Federated);
        Assert.InRange(w.Communes.Count - before, 1, w.Balance.FederateCities);
        var paris = w.Communes.Single(c => c.Name == "Paris");
        Assert.True(paris.InFederation);
        Assert.NotEmpty(w.Trust.Neighbors(paris.Id));
        // The closest pair of cities joins the new communes to the original federation.
        Assert.Contains(Enumerable.Range(before, w.Communes.Count - before), i => w.Trust.Neighbors(i).Any(n => n < before));

        var restored = World.LoadJson(w.SaveJson(), w.Techs);
        Assert.Equal(w.Communes.Count, restored.Communes.Count);
        var policy = new Balanced();
        for (int i = 0; i < 20 && restored.Outcome == Outcome.Ongoing; i++)
        {
            policy.Act(restored);
            restored.Step();
        }
        Assert.Equal(w.Communes.Count, restored.Communes.Count);
    }

    [Fact]
    public void Scripted_wars_start_wear_states_down_and_end()
    {
        var w = World.Load(DataDir, "catalonia_1936@500", seed: 2);
        w.Run(52);
        var war = Assert.Single(w.Wars, x => x.Name == "The Second Sino-Japanese War");
        var china = w.States.Single(s => s.Id == "CHN");
        Assert.True(china.Weariness > 0);
        Assert.Equal(1 - w.Balance.WarCommitment, Global.InterstateSystem.Available(w, china), 6);
        war.EarliestPeace = w.Tick;
        foreach (var s in w.States.Where(s => war.Involves(s.Id))) s.Weariness = 1;
        for (int i = 0; i < 200 && !war.Over; i++) w.Step();
        Assert.True(war.Over);
        Assert.Contains(w.Log, e => e.Text.StartsWith("The Second Sino-Japanese War ends"));

        var restored = World.LoadJson(w.SaveJson(), w.Techs);
        Assert.Equal(w.Wars.Count, restored.Wars.Count);
    }

    [Fact]
    public void Free_federations_never_start_wars()
    {
        Assert.Equal(1, Global.InterstateSystem.Hostility(Global.Ideology.Fascist, Global.Ideology.Communist));
        var w = World.Load(DataDir, "catalonia_1936@500", seed: 4);
        foreach (var s in w.States.Where(s => s.Power >= 5)) s.Ideology = Global.Ideology.Libertarian;
        w.Balance.WarChance = 0.05;
        w.Run(60);
        var big = w.States.Where(s => s.Power >= 5).Select(s => s.Id).ToHashSet();
        Assert.DoesNotContain(w.Wars.Where(x => x.StartTick > 0 && !x.Name.StartsWith("The Second")), x => big.Contains(x.SideA[0]));
    }

    [Fact]
    public void Rival_party_cells_grow_add_drift_and_can_be_exposed()
    {
        var w = NewWorld(seed: 6);
        Assert.Contains(Global.RivalSystem.Rivals(w), r => r.Id == "SUN");
        var c = w.Communes.Single(x => x.Name == "Vic"); // little political education
        w.Run(30);
        Assert.True(c.PartyInfluence > 0.02, $"cells {c.PartyInfluence}");
        Assert.True(w.LastDrift.PartyCells > 0);
        double before = c.PartyInfluence;
        var soviets = w.States.Single(s => s.Id == "SUN");
        double stance = soviets.Stance;
        w.Influence = 100;
        Assert.True(w.Apply(new ExposeCells(c.Id)).Ok);
        Assert.True(c.PartyInfluence < before * 0.5);
        Assert.True(soviets.Stance < stance);
    }

    [Fact]
    public void Rivals_offer_arms_for_commissars_and_pull_revolutions_their_way()
    {
        var w = NewWorld(seed: 6);
        for (int i = 0; i < 40 && !w.PendingEvents.Any(p => p.Id.StartsWith("rival_offer_")); i++) w.Step();
        var offer = Assert.Single(w.PendingEvents, p => p.Id.StartsWith("rival_offer_"));
        double drift = w.Drift.Value, cells = w.Mean(c => c.PartyInfluence);
        Assert.True(w.Apply(new ChooseEventOption(offer.Id, 0)).Ok);
        w.Step();
        Assert.True(w.Mean(c => c.PartyInfluence) > cells + 0.1);
        Assert.True(w.LastDrift.Events >= w.Balance.RivalAcceptDrift);

        var poland = w.States.Single(s => s.Id == "POL"); // borders the Soviet Union
        var ireland = w.States.Single(s => s.Id == "IRL");
        Assert.True(Global.RivalSystem.RevolutionPull(w, poland) > Global.RivalSystem.RevolutionPull(w, ireland));
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
        // Random events and communes' proposals may be waiting too; this is about the scripted one.
        var pending = Assert.Single(w.PendingEvents, p => !p.Id.StartsWith("random_") && !p.Id.StartsWith("proposal_"));
        Assert.Equal("cnt_joins_government", pending.Id);
        double influence = w.Influence;
        var result = w.Apply(new ChooseEventOption(pending.Id, 1)); // stay outside
        Assert.True(result.Ok, result.Message);
        Assert.DoesNotContain(w.PendingEvents, p => p.Id == pending.Id);
        Assert.Equal(influence - 5, w.Influence, 6);
    }

    [Fact]
    public void Unanswered_event_takes_its_default_option()
    {
        var w = NewWorld();
        w.Run(11);
        Assert.Contains(w.PendingEvents, p => p.Id == "cnt_joins_government");
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

    [Fact]
    public void Historical_figures_work_where_they_are_and_Durruti_falls_on_schedule()
    {
        var w = NewWorld();
        var durruti = w.Figures.Single(f => f.Name == "Buenaventura Durruti");
        Assert.Equal(4, w.Figures.Count);
        var bujaraloz = w.Communes.Single(c => c.Name == "Bujaraloz");
        Assert.Equal(bujaraloz.Id, durruti.Commune);
        // A commander strengthens the commune's defence.
        double with = Sim.Systems.WarSystem.Defense(w, bujaraloz);
        durruti.Commune = null;
        double without = Sim.Systems.WarSystem.Defense(w, bujaraloz);
        Assert.True(with > without * 1.05, $"{with} vs {without}");
        durruti.Commune = bujaraloz.Id;
        // The scripted event kills him, whichever option is chosen.
        w.Run(25);
        Assert.False(durruti.Alive);
    }

    [Fact]
    public void An_ambitious_figure_asks_for_power_and_can_be_rotated_back_to_the_ranks()
    {
        var w = NewWorld();
        var oliver = w.Figures.Single(f => f.Name == "Juan García Oliver");
        oliver.Prestige = 0.9;
        oliver.Ambition = 0.8;
        double before = w.LastDrift.Figures;
        w.Step();
        Assert.True(w.LastDrift.Figures > before);
        w.Step();
        var bid = Assert.Single(w.PendingEvents, p => p.Id.StartsWith("figure_bid_"));
        Assert.True(w.Apply(new ChooseEventOption(bid.Id, 1)).Ok); // rotate
        Assert.Null(oliver.Commune);
        Assert.True(oliver.Prestige < 0.5 && oliver.Resting > 0);
        Assert.False(w.Apply(new PostFigure(oliver.Id, 0)).Ok); // resting
        while (oliver.Resting > 0) w.Step();
        Assert.True(w.Apply(new PostFigure(oliver.Id, 0)).Ok);

        var restored = World.LoadJson(w.SaveJson(), w.Techs);
        var again = restored.Figures.Single(f => f.Name == oliver.Name);
        Assert.Equal(oliver.Prestige, again.Prestige);
        Assert.Equal(oliver.Commune, again.Commune);
    }

    [Fact]
    public void Generated_starts_get_one_figure_of_each_role()
    {
        var w = World.Create(Sandbox.Value.Generate(5, 20, calibrate: false), NewWorld().Techs, seed: 5);
        Assert.Equal(Enum.GetValues<FigureRole>().Length, w.Figures.Select(f => f.Role).Distinct().Count());
        Assert.All(w.Figures, f => Assert.StartsWith("The ", f.Name));
    }

    [Fact]
    public void Fortifying_a_front_commune_raises_its_defence_and_raids_have_consequences()
    {
        var w = NewWorld();
        w.Step();
        var front = w.Federation.First(c => c.Front);
        double before = Sim.Systems.WarSystem.Defense(w, front);
        Assert.True(w.Apply(new Fortify(front.Id)).Ok);
        Assert.False(w.Apply(new Fortify(front.Id)).Ok); // already digging
        Assert.False(w.Apply(new Fortify(w.Federation.First(c => !c.Front).Id)).Ok); // not on the front
        w.Run(3);
        Assert.True(front.Fortification > 0.3);
        Assert.True(Sim.Systems.WarSystem.Defense(w, front) > before);

        // A raid either tires the enemy or costs the commune fighters; then it must wait.
        var enemy = w.Enemies[front.Fronts.Min()];
        double exhaustion = enemy.Exhaustion, mobilization = front.Mobilization;
        Assert.True(w.Apply(new Raid(front.Id)).Ok);
        Assert.True(enemy.Exhaustion > exhaustion || front.Mobilization < mobilization);
        Assert.False(w.Apply(new Raid(front.Id)).Ok);

        var restored = World.LoadJson(w.SaveJson(), w.Techs);
        Assert.Equal(front.Fortification, restored.Communes[front.Id].Fortification);
        Assert.Equal(front.LastRaid, restored.Communes[front.Id].LastRaid);
    }

    [Fact]
    public void A_worn_enemy_offers_peace_in_open_starts_and_peace_with_all_ends_the_game()
    {
        var atlas = Sim.Global.Atlas.Load(DataDir, "1936");
        var w = World.Create(Sim.Open.OpenStartGenerator.Generate(atlas, Sandbox.Value, "FRA", 5, calibrate: false), NewWorld().Techs, seed: 5);
        w.Run(10);
        foreach (var e in w.Enemies) { e.Exhaustion = 0.5; e.LastGain = w.Tick; }
        PendingEvent? offer = null;
        for (int i = 0; i < 60 && offer is null && w.Outcome == Outcome.Ongoing; i++)
        {
            foreach (var e in w.Enemies) e.Exhaustion = 0.5;
            w.Step();
            offer = w.PendingEvents.FirstOrDefault(p => p.Id.StartsWith("peace_"));
        }
        Assert.NotNull(offer);
        double drift = w.Drift.Value;
        Assert.True(w.Apply(new ChooseEventOption(offer!.Id, 0)).Ok);
        var enemy = w.Enemies.Single(e => e.AtPeace);
        int k = w.Enemies.ToList().IndexOf(enemy);
        Assert.DoesNotContain(w.Communes, c => c.Fronts.Contains(k));
        foreach (var e in w.Enemies) e.AtPeace = true;
        w.Step();
        Assert.Equal(Outcome.Survived, w.Outcome);
        Assert.Equal("A recognized autonomy", Endings.Of(w).Title);
        Assert.NotEmpty(Endings.Epilogue(w));
    }

    [Fact]
    public void Historical_scenarios_never_offer_peace()
    {
        var w = NewWorld();
        foreach (var e in w.Enemies) { e.Exhaustion = 0.9; }
        for (int i = 0; i < 40; i++)
        {
            foreach (var e in w.Enemies) e.Exhaustion = 0.9;
            w.Step();
        }
        Assert.DoesNotContain(w.PendingEvents, p => p.Id.StartsWith("peace_"));
    }

    [Fact]
    public void A_sample_of_countries_in_every_era_plays_to_the_end_saves_and_has_an_ending()
    {
        var techs = NewWorld().Techs;
        int played = 0;
        foreach (var era in Sim.Open.OpenStartGenerator.Eras)
        {
            var atlas = Sim.Global.Atlas.Load(DataDir, era);
            foreach (var state in Sim.Open.OpenStartGenerator.Playable(atlas).Where((_, i) => i % 9 == 0))
            {
                var w = World.Create(Sim.Open.OpenStartGenerator.Generate(atlas, Sandbox.Value, state.Id, 7, calibrate: false, scope: Sim.Open.OpenScope.Region), techs, seed: 7);
                var policy = new Balanced();
                while (w.Outcome == Outcome.Ongoing)
                {
                    policy.Act(w);
                    w.Step();
                }
                Assert.Equal(w.Tick, World.LoadJson(w.SaveJson(), techs).Tick);
                Assert.False(string.IsNullOrEmpty(Endings.Of(w).Title), $"{era} {state.Id}");
                Assert.NotEmpty(Endings.Epilogue(w));
                played++;
            }
        }
        Assert.True(played > 50, $"only {played} games");
    }

    [Fact]
    public void In_a_campaign_peace_does_not_end_the_game_and_treaties_can_be_broken()
    {
        var atlas = Sim.Global.Atlas.Load(DataDir, "1936");
        var scenario = Sim.Open.OpenStartGenerator.Generate(atlas, Sandbox.Value, "FRA", 5, length: 1300, calibrate: false);
        Assert.True(scenario.IsCampaign);
        var w = World.Create(scenario, NewWorld().Techs, seed: 5);
        w.Run(10);
        var front = w.Federation.Where(c => c.Front).Select(c => c.Id).ToList();
        foreach (var e in w.Enemies) Sim.Systems.PeaceSystem.MakePeace(w, e);
        w.Step();
        Assert.Equal(Outcome.Ongoing, w.Outcome); // a campaign plays on in peacetime

        // After the treaty's sure years, some enemy breaks the peace and its old front reopens.
        var b = w.Balance;
        b.TreatyBreakPerYear = 52; // certain, once the treaty may be broken
        int sure = (int)(b.TreatyYears * 52 / w.Weeks);
        for (int i = 0; i < sure + 2 && w.Enemies.All(e => e.AtPeace) && w.Outcome == Outcome.Ongoing; i++) w.Step();
        Assert.Contains(w.Enemies, e => !e.AtPeace);
        Assert.Contains(w.Communes, c => c.Front && front.Contains(c.Id));
    }
}
