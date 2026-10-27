namespace Politicom.Sim;

/// <summary>One part of a number: what it is, and how much it adds (or, for a factor, multiplies).</summary>
public readonly record struct Part(string Label, double Value);

/// <summary>
/// Why a commune's numbers are what they are, part by part, as Victoria 3's tooltips break a
/// value into its causes. The simulation computes these numbers through the same methods, so
/// the explanation and the number can never disagree.
/// </summary>
public static class Explain
{
    /// <summary>What wellbeing moves toward (it closes 15% of the gap each turn), part by part.</summary>
    public static List<Part> Wellbeing(World w, Commune c)
    {
        var b = w.Balance;
        double foodSecurity = c.Starving ? 0 : Math.Clamp(c.Food / (c.Population * b.FoodNeed * 2), 0, 1);
        double goodsSecurity = c.GoodsShort ? 0 : Math.Clamp(c.Goods / (c.Population * b.GoodsNeed * 2), 0, 1);
        var parts = new List<Part>
        {
            new("Base", 0.2),
            new(c.Starving ? "Food: starving" : "Food in store", 0.4 * foodSecurity),
            new(c.GoodsShort ? "Goods: short" : "Goods in store", 0.2 * goodsSecurity),
            new("Political education", 0.1 * c.PoliticalEducation),
            new("Practical education", 0.1 * c.PracticalEducation),
        };
        int stage = (int)w.Drift.Stage;
        if (stage > 0) parts.Add(new($"Hierarchy ({DriftEffects.Name(w.Drift.Stage)})", -0.04 * stage));
        if (c.Front) parts.Add(new("Ground lost at the front", -0.15 * (1 - c.Control)));
        if (w.RecallPenaltyTicks > 0) parts.Add(new("Delegates just recalled", -0.03));
        if (c.Kind == CommuneKind.Industrial && w.FoodPrice > 1) parts.Add(new("Dear food in a factory town", -b.DearFoodWellbeing * (w.FoodPrice - 1)));
        return parts;
    }

    /// <summary>Discontent, part by part: above about 0.6 a commune may vote to leave.</summary>
    public static List<Part> Discontent(World w, Commune c)
    {
        double trust = w.Trust.AverageTrust(c.Id, n => w.Communes[n].InFederation);
        var parts = new List<Part>
        {
            new($"Little trust in its neighbours ({trust:P0})", 0.45 * (1 - trust)),
            new($"Low wellbeing ({c.Wellbeing:P0})", 0.25 * (1 - c.Wellbeing)),
            new("Values: little taste for federation", 0.15 * (1 - CommuneValues.To01(c.Values.Federalism))),
        };
        double stage = DriftEffects.SecessionPressure(w.Drift.Stage);
        if (stage != 0) parts.Add(new($"Hierarchy ({DriftEffects.Name(w.Drift.Stage)})", stage));
        if (c.Starving) parts.Add(new("Starving", 0.1));
        return parts;
    }

    /// <summary>
    /// Defence at the front: the commune's own militia and its neighbours' support (added), then the
    /// factors that multiply them.
    /// </summary>
    public static (List<Part> Added, List<Part> Factors) Defense(World w, Commune c)
    {
        var b = w.Balance;
        var added = new List<Part> { new("Its own militia", Systems.WarSystem.Militia(w, c)) };
        double support = 0;
        foreach (var n in w.Trust.Neighbors(c.Id))
        {
            var d = w.Communes[n];
            if (d.Occupied || d.Front) continue;
            support += w.Trust[c.Id, n] * Systems.WarSystem.Militia(w, d) * b.NeighborSupport * (d.Federated ? 1 : 0.3) * (w.Rail(c.Id, n) ? b.RailSupport : 1);
        }
        added.Add(new("Neighbours' militias, by trust and rail", support));
        var factors = new List<Part>();
        void F(string label, double factor)
        {
            if (Math.Abs(factor - 1) > 1e-6) factors.Add(new(label, factor));
        }
        F("Techniques", 1 + w.LocalEffect(c, e => e.Defense));
        F($"Hierarchy ({DriftEffects.Name(w.Drift.Stage)})", 1 + DriftEffects.Defense(w.Drift.Stage));
        F("War committee", w.WarCommittee ? 1 + b.WarCommitteeDefense : 1);
        F("Government militarization", w.Ally.MilitarizationTicks > 0 ? 1 + b.MilitarizationDefense : 1);
        F("Arms (goods coverage)", b.ArmsShortageDefense + (1 - b.ArmsShortageDefense) * c.GoodsCoverage);
        F("Stockpiles", 1 + b.StockpileDefense * Math.Clamp(c.Goods / (c.Population * b.StockpilePerPop), 0, 1));
        F("Delegates just recalled", w.RecallPenaltyTicks > 0 ? 0.9 : 1);
        F("Foreign volunteers", 1 + w.VolunteerDefense);
        F("Commanders", 1 + Systems.FigureSystem.Defense(w, c));
        F($"Fortifications ({c.Fortification:P0})", 1 + b.FortDefense * c.Fortification);
        return (added, factors);
    }

    public static double Sum(IEnumerable<Part> parts) => parts.Sum(p => p.Value);
    public static double Product(IEnumerable<Part> parts) => parts.Aggregate(1.0, (a, p) => a * p.Value);
}
