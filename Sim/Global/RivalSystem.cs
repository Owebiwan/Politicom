namespace Politicom.Sim.Global;

/// <summary>
/// Ideological rivals: authoritarian communist states compete with the movement for the same
/// workers. They build party cells in the communes (which push drift up), offer arms in exchange
/// for discipline, and tilt revolutions abroad toward a party seizing power.
/// </summary>
public static class RivalSystem
{
    /// <summary>States that act as rivals: authoritarian communist, interested in the federation.</summary>
    public static IEnumerable<WorldState> Rivals(World w) =>
        w.States.Where(s => s.Ideology == Ideology.Communist && s.Weight >= 0.3 && s.Stance > -0.3);

    /// <summary>How hard rivals are pushing: their weight, scaled by how close they are to you.</summary>
    public static double Pressure(World w) => Rivals(w).Sum(s => s.Weight * (0.5 + Math.Max(0, s.Stance)));

    internal static void Run(World w)
    {
        if (w.States.Count == 0) return;
        var b = w.Balance;
        double pressure = Pressure(w);

        // Party cells grow where political education is thin and life is hard; education wears them down.
        foreach (var c in w.Federation)
        {
            double grow = b.PartyGrowth * pressure * (1 - c.PoliticalEducation) * (c.Starving || c.Front ? 1.5 : 1) * (Systems.StruggleSystem.Hostile(w) ? 1.3 : 1)
                          * (1 - b.CultureResistsParty * c.Culture);
            double fade = b.PartyFade * c.PoliticalEducation;
            c.PartyInfluence = Math.Clamp(c.PartyInfluence + grow * (1 - c.PartyInfluence) - fade * c.PartyInfluence, 0, 1);
        }

        // Offers of arms for discipline, from the strongest rival, now and then.
        var rival = Rivals(w).Where(s => s.Weight >= 0.5).OrderByDescending(s => s.Weight).FirstOrDefault();
        bool pending = w.PendingEvents.Any(p => p.Id.StartsWith("rival_offer_"));
        if (rival != null && !pending && w.Tick > 0 && w.Tick % b.RivalOfferInterval == b.RivalOfferInterval / 2 && w.Federation.Any(c => c.Front))
            Offer(w, rival);
    }

    /// <summary>
    /// A rival's offer of arms. Each time it comes back the terms are harder (more arms, but more
    /// drift and a bigger party); the third is its last.
    /// </summary>
    private static void Offer(World w, WorldState rival)
    {
        var b = w.Balance;
        int before = w.Scenario.Events.Count(e => e.Id.StartsWith($"rival_offer_{rival.Id}_"));
        if (before >= 3) return;
        double harder = 1 + 0.5 * before;
        string country = StateText.Country(rival.Name);
        string terms = w.Year >= 1914
            ? $"Advisers from {country} offer tanks and aircraft for the front — if the militias accept their commissars and the party gets a free hand."
            : $"Envoys from {country} offer arms and drillmasters for the front — if the militias accept their overseers and the elect get a free hand.";
        w.Scenario.Events.Add(new EventDefinition
        {
            Id = $"rival_offer_{rival.Id}_{w.Tick}",
            Tick = w.Tick + 1,
            Title = before switch { 0 => $"{rival.Name} offers arms", 1 => $"{rival.Name} offers arms again", _ => $"{rival.Name}'s last offer" },
            Text = before switch
            {
                0 => terms,
                1 => $"The envoys are back, with more to give and more to ask. {terms} This time they also want the federation's food committees answering to them.",
                _ => $"A last offer, they say, and they mean it. {terms} They want the police, the press and the food committees. Refuse, and there will be no more offers.",
            },
            DefaultOption = 1,
            Options =
            {
                new EventOption
                {
                    Label = "Accept the arms",
                    Description = w.Year >= 1914 ? "The front gets modern weapons. The party gets its commissars." : "The front gets arms. The elect get their overseers.",
                    Effects = new EventEffects
                    {
                        GoodsToFront = Math.Round(b.RivalArms * (1 + 0.3 * before)), MilitarizationTicks = 8, Tension = -8,
                        Drift = Math.Round(b.RivalAcceptDrift * harder, 1),
                        PartyInfluence = Math.Round(0.15 * harder, 2), Stances = { [rival.Id] = 0.15 },
                    },
                },
                new EventOption
                {
                    Label = "Refuse",
                    Description = "No commissars. No tanks either.",
                    Effects = new EventEffects { Stances = { [rival.Id] = -0.1 }, Tension = 5 },
                },
            },
        });
    }

    /// <summary>Extra chance that a winning revolution ends with a party in power, from rivals nearby.</summary>
    public static double RevolutionPull(World w, WorldState s) =>
        Math.Min(0.4, 0.06 * w.States.Where(r => r.Ideology == Ideology.Communist && r != s)
            .Sum(r => r.Power * (s.Neighbours.Contains(r.Id) ? 1 : 0.3)));
}
