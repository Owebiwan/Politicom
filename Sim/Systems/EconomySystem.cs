namespace Politicom.Sim.Systems;

/// <summary>
/// Production, mutual aid between neighbours, the federal surplus pool, shortages and wellbeing.
/// There is no tax: everything that moves between communes moves because they chose to share it.
/// </summary>
internal static class EconomySystem
{
    public static void Run(World w)
    {
        Produce(w);
        Trade(w);
        foreach (var food in new[] { true, false })
        {
            NeighborAid(w, food);
            FederalPool(w, food);
        }
        Shortages(w);
        Ledgers(w);
        Wellbeing(w);
    }

    private static void Produce(World w)
    {
        var b = w.Balance;
        var stage = w.Drift.Stage;
        Imports(w);
        foreach (var c in w.Active)
        {
            double labor = Math.Max(0, 1 - c.PracticalSchooling - c.PoliticalSchooling - b.ProjectLabor * w.ProjectsJoinedBy(c.Id));
            double trust = 1 + b.TrustProductivity * (w.Trust.AverageTrust(c.Id) - 0.5) * 2;
            double skill = 1 + b.PracticalProductivity * c.PracticalEducation;
            double morale = 0.7 + 0.3 * c.Wellbeing;
            double recall = w.RecallPenaltyTicks > 0 ? 0.95 : 1;
            double m = labor * trust * skill * morale * c.Control * recall * DriftEffects.Productivity(stage) * (1 + c.ProductivityBonus);

            var (foodYield, goodsYield) = c.Kind switch
            {
                CommuneKind.Agrarian => (b.AgrarianFoodYield, b.AgrarianGoodsYield),
                CommuneKind.Industrial => (b.IndustrialFoodYield, b.IndustrialGoodsYield),
                _ => (b.MixedFoodYield, b.MixedGoodsYield),
            };
            c.FoodProduced = c.Population * foodYield * m * (1 + w.LocalEffect(c, e => e.FoodProduction));
            c.GoodsProduced = c.Population * goodsYield * m * (1 + w.LocalEffect(c, e => e.GoodsProduction));
            c.Food += c.FoodProduced - c.Population * b.FoodNeed;
            c.Goods += c.GoodsProduced - GoodsDemand(w, c);
        }
    }

    /// <summary>
    /// Selling surplus food abroad for goods and arms. Capitalist states want currency, so trading
    /// brings markets and accounts back in (drift, see DriftSystem); and the further the idea
    /// spreads, the more of them embargo the federation.
    /// </summary>
    private static void Trade(World w)
    {
        w.LastExports = w.LastImports = 0;
        if (!w.ForeignTrade) return;
        var b = w.Balance;
        var members = w.Federation.ToList();
        foreach (var c in members)
        {
            double surplus = c.Food - Reserve(b, c, true);
            if (surplus <= 0) continue;
            double sold = surplus * b.TradeShare;
            c.Food -= sold;
            w.LastExports += sold;
        }
        w.LastImports = w.LastExports * b.TradeRate * (1 - w.Embargo);
        if (w.LastImports <= 0) return;
        // Arms go to the front first, the rest by population.
        var fronts = members.Where(c => c.Front).ToList();
        var receivers = fronts.Count > 0 ? fronts : members;
        double population = receivers.Sum(c => c.Population);
        foreach (var c in receivers) c.Goods += w.LastImports * c.Population / population;
    }

    /// <summary>Supplies from outside (e.g. Paris fed through the Prussian lines), shared by population.</summary>
    private static void Imports(World w)
    {
        var s = w.Scenario;
        if (s.FoodImports <= 0 && s.GoodsImports <= 0) return;
        var members = w.Federation.ToList();
        double population = members.Sum(c => c.Population);
        if (population <= 0) return;
        foreach (var c in members)
        {
            c.Food += s.FoodImports * c.Population / population;
            c.Goods += s.GoodsImports * c.Population / population;
        }
    }

    /// <summary>Neighbours with surplus help neighbours in deficit, more readily the more they trust them.</summary>
    private static void NeighborAid(World w, bool food)
    {
        var b = w.Balance;
        double trustGain = b.AidTrustGain * DriftEffects.TrustGrowth(w.Drift.Stage) * (1 + w.SocietyEffect(e => e.TrustGrowth));
        var needy = w.Active.Where(c => Stock(c, food) < 0).OrderBy(c => Stock(c, food) / c.Population).ToList();
        foreach (var r in needy)
        {
            foreach (var dId in w.Trust.Neighbors(r.Id).OrderByDescending(n => w.Trust[r.Id, n]))
            {
                double deficit = -Stock(r, food);
                if (deficit <= 0) break;
                var d = w.Communes[dId];
                if (d.Occupied) continue;
                double surplus = Stock(d, food) - Reserve(b, d, food);
                if (surplus <= 0) continue;
                double generosity = b.NeighborGenerosity * Generosity(d) * w.Trust[d.Id, r.Id];
                if (d.Federated != r.Federated) generosity *= 0.5;
                double amount = Math.Min(deficit, surplus * generosity);
                if (amount <= 1e-6) continue;
                Transfer(d, r, amount, food);
                w.Trust.Add(d.Id, r.Id, trustGain * amount / r.Population);
            }
        }
    }

    /// <summary>
    /// Federated communes put part of their surplus into a common pool in proportion to how much
    /// they trust their neighbours. This is how Aragonese grain reaches Barcelona's factories.
    /// </summary>
    private static void FederalPool(World w, bool food)
    {
        var b = w.Balance;
        var members = w.Federation.ToList();
        var givers = new List<(Commune C, double Amount)>();
        double pool = 0;
        foreach (var m in members)
        {
            double surplus = Stock(m, food) - Reserve(b, m, food);
            if (surplus <= 0) continue;
            double give = surplus * b.PoolShare * w.Trust.AverageTrust(m.Id) * Math.Min(1, Generosity(m));
            if (give <= 1e-6) continue;
            AddStock(m, food, -give);
            givers.Add((m, give));
            pool += give;
        }
        if (pool <= 0) return;

        pool *= 1 - b.PoolLoss * (1 - w.TransportBonus);
        var deficits = members.Where(m => Stock(m, food) < 0).ToList();
        double totalDeficit = deficits.Sum(m => -Stock(m, food));
        double distributed = 0;
        double trustGain = b.AidTrustGain * 0.3 * DriftEffects.TrustGrowth(w.Drift.Stage);
        foreach (var r in deficits)
        {
            double amount = Math.Min(-Stock(r, food), pool * -Stock(r, food) / totalDeficit);
            AddStock(r, food, amount);
            r.AidReceived += amount;
            distributed += amount;
            foreach (var n in w.Trust.Neighbors(r.Id)) w.Trust.Add(r.Id, n, trustGain * amount / r.Population);
        }

        // Whatever was not needed goes back to those who gave it.
        double leftover = pool - distributed;
        double totalGiven = givers.Sum(g => g.Amount);
        foreach (var (c, amount) in givers)
        {
            double back = leftover * amount / totalGiven;
            AddStock(c, food, back);
            c.AidGiven += amount - back;
        }
    }

    private static void Shortages(World w)
    {
        var b = w.Balance;
        foreach (var c in w.Active)
        {
            c.Starving = c.Food < 0;
            if (c.Starving)
            {
                double severity = Math.Min(1, -c.Food / (c.Population * b.FoodNeed));
                c.Population *= 1 - b.FamineMortality * severity;
                c.Wellbeing -= 0.05 * severity;
                // Neighbours who sat on full granaries while this commune starved lose its trust.
                foreach (var n in w.Trust.Neighbors(c.Id))
                {
                    var d = w.Communes[n];
                    if (!d.Occupied && d.Food > Reserve(b, d, true) * 1.5) w.Trust.Add(c.Id, n, -b.HoardingTrustLoss * severity);
                }
                c.Food = 0;
            }

            c.GoodsShort = c.Goods < 0;
            c.GoodsCoverage = c.GoodsShort ? Math.Clamp(1 + c.Goods / GoodsDemand(w, c), 0, 1) : 1;
            if (c.GoodsShort)
            {
                c.Wellbeing -= 0.02 * (1 - c.GoodsCoverage);
                c.Goods = 0;
            }

            c.Food = Math.Min(c.Food, c.Population * b.FoodNeed * b.StorageTicks);
            c.Goods = Math.Min(c.Goods, c.Population * b.GoodsNeed * b.StorageTicks);
        }
    }

    /// <summary>
    /// Free riders take more than they give. Individualist communes with little political
    /// education are likeliest to; labor-time accounting and (grimly) police reduce it.
    /// </summary>
    private static void Ledgers(World w)
    {
        double accounting = Math.Clamp(w.SocietyEffect(e => e.FreeRiding), 0, 0.9);
        double stage = DriftEffects.FreeRiding(w.Drift.Stage);
        foreach (var c in w.Active)
        {
            c.AidGiven *= 0.95;
            c.AidReceived *= 0.95;
            double net = Math.Clamp((c.AidReceived - c.AidGiven) / (c.Population * 2), 0, 1);
            double individualism = 1 - CommuneValues.To01(c.Values.Collectivism);
            c.FreeRiding = net * (0.4 + 0.6 * individualism) * (1 - 0.5 * c.PoliticalEducation) * (1 - accounting) * stage;
        }
    }

    private static void Wellbeing(World w)
    {
        var b = w.Balance;
        int stage = (int)w.Drift.Stage;
        foreach (var c in w.Active)
        {
            double foodSecurity = c.Starving ? 0 : Math.Clamp(c.Food / (c.Population * b.FoodNeed * 2), 0, 1);
            double goodsSecurity = c.GoodsShort ? 0 : Math.Clamp(c.Goods / (c.Population * b.GoodsNeed * 2), 0, 1);
            double target = 0.2 + 0.4 * foodSecurity + 0.2 * goodsSecurity
                            + 0.1 * c.PoliticalEducation + 0.1 * c.PracticalEducation
                            - 0.04 * stage
                            - (c.Front ? 0.15 * (1 - c.Control) : 0)
                            - (w.RecallPenaltyTicks > 0 ? 0.03 : 0);
            c.Wellbeing = Math.Clamp(c.Wellbeing + (target - c.Wellbeing) * 0.15, 0, 1);
        }
    }

    /// <summary>Weekly goods need: upkeep, plus arms for front communes (sized by last week's pressure).</summary>
    internal static double GoodsDemand(World w, Commune c) =>
        c.Population * (w.Balance.GoodsNeed + w.LocalEffect(c, e => e.GoodsUpkeep))
        + (c.Front ? w.Balance.ArmsUse * c.Pressure : 0);

    internal static double Generosity(Commune c) =>
        (0.6 + 0.4 * CommuneValues.To01(c.Values.Collectivism)) * (1 + 0.5 * c.PoliticalEducation);

    internal static double Reserve(Balance b, Commune c, bool food) =>
        c.Population * (food ? b.FoodNeed : b.GoodsNeed) * b.ReserveTicks;

    private static double Stock(Commune c, bool food) => food ? c.Food : c.Goods;

    private static void AddStock(Commune c, bool food, double amount)
    {
        if (food) c.Food += amount;
        else c.Goods += amount;
    }

    private static void Transfer(Commune from, Commune to, double amount, bool food)
    {
        AddStock(from, food, -amount);
        AddStock(to, food, amount);
        from.AidGiven += amount;
        to.AidReceived += amount;
    }
}
