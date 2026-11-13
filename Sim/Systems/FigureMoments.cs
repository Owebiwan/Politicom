namespace Politicom.Sim.Systems;

/// <summary>
/// The figures act in character. Every couple of months one of them does something the movement
/// must answer: a commander wants to strike back from a front that holds, or refuses to give
/// ground at one that doesn't; an orator offers to speak to a discontented town, or to denounce
/// the party; an organizer proposes requisitions for a hungry commune, or double shifts for the
/// front; an educator wants a newspaper, or writes against an ambitious comrade; two figures
/// quarrel. What the movement answers changes the war, the communes and the figures themselves:
/// backing a figure feeds their prestige and their ambition.
/// </summary>
internal static class FigureMoments
{
    public static void Run(World w)
    {
        var b = w.Balance;
        if (w.Tick < 4 || (w.Tick - w.LastFigureMoment) * w.Weeks < b.FigureMomentGap) return;
        if (!w.Rng.Chance(b.FigureMomentRate * w.Weeks)) return;
        if (w.PendingEvents.Any(p => p.Id.StartsWith("figure_"))) return;
        var options = Candidates(w).Where(m => !Recent(w, m.Id)).ToList();
        if (options.Count == 0) return;
        var moment = options[w.Rng.Next(options.Count)];
        moment.Id = $"figure_moment_{moment.Id}_{w.Tick}";
        moment.Tick = w.Tick + 1;
        moment.ExpiresAfter = 4;
        w.Scenario.Events.Add(moment);
        w.LastFigureMoment = w.Tick;
    }

    /// <summary>The same figure doing the same thing again within a year would be one too many.</summary>
    private static bool Recent(World w, string key) =>
        w.Scenario.Events.Any(e => e.Id.StartsWith($"figure_moment_{key}_") && (w.Tick - e.Tick) * w.Weeks < 52);

    private static IEnumerable<EventDefinition> Candidates(World w)
    {
        var b = w.Balance;
        double blow = w.Enemies.Count > 0 ? Math.Max(0.05, w.Enemies.Where(e => !e.AtPeace).Select(e => e.Strength).DefaultIfEmpty(0).Max() * 0.08) : 0;
        var atWork = w.Figures.Where(f => f.Alive && f.Commune is int id && w.Communes[id].InFederation).ToList();
        foreach (var f in atWork)
        {
            var c = w.Communes[f.Commune!.Value];
            string role = Figure.RoleName(f.Role);
            switch (f.Role)
            {
                case FigureRole.Commander when c.Front && c.Defense > c.Pressure && blow > 0:
                    yield return Moment($"strike_{f.Id}", f, $"{f.Name} wants to strike back",
                        $"The line at {c.Name} is holding, and {f.Name} wants to take the fight to the enemy: a night attack on its forward positions. " +
                        "The column is eager. It will cost lives, and a commander who wins it will be talked about.",
                        ("Let them attack", "A blow to the enemy, and to its morale. The commander's name grows.",
                            new EventEffects { EnemyStrength = -blow, EnemyExhaustion = 0.05, FigurePrestige = 0.12, FigureAmbition = 0.05, FigureRisk = 0.08, Communes = new[] { c.Name }, Population = -0.005 }),
                        ("Hold the line", "No risks. The column grumbles that the assemblies are timid.",
                            new EventEffects { FigurePrestige = -0.03, Communes = new[] { c.Name }, Wellbeing = -0.02 }));
                    break;
                case FigureRole.Commander when c.Front && c.Pressure > c.Defense:
                    yield return Moment($"stand_{f.Id}", f, $"{f.Name} will not fall back",
                        $"{c.Name} is giving way. The defence committee wants an orderly retreat to the next line; {f.Name} refuses, and says the column will hold the town street by street.",
                        ("Stand and fight", "Ground held for now, at a cost in lives; the commander is in the thick of it.",
                            new EventEffects { Control = 0.1, Communes = new[] { c.Name }, Population = -0.01, Enthusiasm = 0.03, FigurePrestige = 0.1, FigureAmbition = 0.04, FigureRisk = 0.1 }),
                        ("Fall back in good order", "The column lives to fight again; the town gives ground.",
                            new EventEffects { Control = -0.06, Communes = new[] { c.Name }, FigurePrestige = -0.05 }));
                    break;
                case FigureRole.Orator:
                    if (w.Federation.Where(d => d.Discontent > 0.4).OrderByDescending(d => d.Discontent).FirstOrDefault() is { } unhappy)
                        yield return Moment($"speak_{f.Id}", f, $"{f.Name} offers to speak in {unhappy.Name}",
                            $"{unhappy.Name} is restless: its assembly talks of going its own way. {f.Name} offers to go and speak to its assembly.",
                            ("Send them", "Hearts are won back, for a while; and the crowd's love goes to the speaker.",
                                new EventEffects { Communes = new[] { unhappy.Name }, Trust = 0.06, Wellbeing = 0.05, Influence = -4, FigurePrestige = 0.08, FigureAmbition = 0.05 }),
                            ("Let the assembly settle it", "Slower, and nobody's triumph. Its people learn to argue it out.",
                                new EventEffects { Communes = new[] { unhappy.Name }, PoliticalEducation = 0.02 }));
                    if (w.Federation.Any() && w.Federation.Average(d => d.PartyInfluence) > 0.2)
                        yield return Moment($"denounce_{f.Id}", f, $"{f.Name} denounces the party",
                            $"{f.Name} wants to speak out against the party's cells: their commissars, their lists, their meetings behind closed doors. Others plead for unity while the war lasts.",
                            ("Let them speak", "The cells lose ground everywhere; the party and its patrons are furious.",
                                new EventEffects { PartyInfluence = -0.15, FigurePrestige = 0.1, FigureAmbition = 0.03, Enthusiasm = -0.02 }),
                            ("Keep the unity", "No public quarrel. The cells go on growing quietly.",
                                new EventEffects { PartyInfluence = 0.04, Enthusiasm = 0.02 }));
                    break;
                case FigureRole.Organizer:
                    if (w.Federation.FirstOrDefault(d => d.Starving) is { } hungry)
                        yield return Moment($"requisition_{f.Id}", f, $"{f.Name} proposes requisitions for {hungry.Name}",
                            $"{hungry.Name} is going hungry. {f.Name} proposes that a food committee requisition grain from the better-off communes and send it there, quickly.",
                            ("Requisition", "Grain arrives at once, and committees learn to take: drift. The organizer's hand grows stronger.",
                                new EventEffects { Communes = new[] { hungry.Name }, Food = 6, Drift = 1.5, FigurePrestige = 0.08, FigureAmbition = 0.08 }),
                            ("Appeal for voluntary sharing", "Less, and slower, but given freely.",
                                new EventEffects { Communes = new[] { hungry.Name }, Food = 3, Trust = 0.02, Influence = -5 }));
                    if (w.Federation.Any(d => d.Front))
                        yield return Moment($"shifts_{f.Id}", f, $"{f.Name} calls for double shifts",
                            $"The front is short of everything. {f.Name} asks the workshops to work double shifts for the militias until the crisis passes.",
                            ("Double shifts", "Arms and supplies for the front; tired, unhappy workers.",
                                new EventEffects { GoodsToFront = 40 + 6 * w.Federation.Count(), Wellbeing = -0.04, FigurePrestige = 0.06, FigureAmbition = 0.05 }),
                            ("Keep the eight-hour day", "The workshops keep their hours; the movement keeps its promise.",
                                new EventEffects { Enthusiasm = 0.02, FigurePrestige = -0.02 }));
                    break;
                case FigureRole.Educator:
                    var rival = atWork.Where(o => o != f && o.Hunger >= 0.25).OrderByDescending(o => o.Hunger).FirstOrDefault();
                    if (rival != null)
                        yield return Moment($"pamphlet_{f.Id}_{rival.Id}", f, $"{f.Name} writes against {rival.Name}",
                            $"{f.Name} has written a pamphlet against {rival.Name}: the {Figure.RoleName(rival.Role)} is gathering a following, it says, and a following is how a revolution grows a master.",
                            ("Print it", $"{rival.Name}'s standing falls, and so does drift; their followers are bitter.",
                                new EventEffects { Rival = rival.Name, RivalPrestige = -0.15, RivalAmbition = -0.12, Drift = -2, FigurePrestige = 0.05,
                                    // Their followers, where they work, take it badly.
                                    Communes = new[] { w.Communes[rival.Commune!.Value].Name }, Wellbeing = -0.03 }),
                            ("Ask them to hold it", "No quarrel in public. Nothing checks the rival.",
                                new EventEffects { FigurePrestige = -0.02 }));
                    else
                        yield return Moment($"paper_{f.Id}", f, $"{f.Name} wants to found a newspaper",
                            $"{f.Name} wants to found a newspaper in {c.Name}: news from the front, debates from the assemblies, poems, a page for the children.",
                            ("Fund it", "Political education and culture rise around it, and so does its editor's name.",
                                new EventEffects { Communes = new[] { c.Name }, Influence = -6, PoliticalEducation = 0.05, Culture = 0.08, FigurePrestige = 0.05, FigureAmbition = 0.02 }),
                            ("Not now", "Paper and ink go to the front.", new EventEffects()));
                    break;
            }
        }

        // Two prestigious figures fall out.
        var proud = atWork.Where(f => f.Prestige >= 0.4).OrderByDescending(f => f.Prestige).Take(2).ToList();
        if (proud.Count == 2)
        {
            var (x, y) = (proud[0], proud[1]);
            yield return Moment($"quarrel_{x.Id}_{y.Id}", x, $"{x.Name} and {y.Name} fall out",
                $"{x.Name} and {y.Name} are quarrelling in public over who speaks for the movement, and their followers are taking sides.",
                ($"Side with {x.Name}", $"{y.Name} loses standing; their followers sulk.",
                    new EventEffects { FigurePrestige = 0.05, Rival = y.Name, RivalPrestige = -0.12, Trust = -0.02 }),
                ($"Side with {y.Name}", $"{x.Name} loses standing; their followers sulk.",
                    new EventEffects { FigurePrestige = -0.12, Rival = y.Name, RivalPrestige = 0.05, Trust = -0.02 }),
                ("Put it to the congress", "The delegates decide, not the famous: both lose a little of their shine.",
                    new EventEffects { Influence = -5, FigurePrestige = -0.04, FigureAmbition = -0.05, Rival = y.Name, RivalPrestige = -0.04, RivalAmbition = -0.05, PoliticalEducation = 0.01 }));
        }
    }

    private static EventDefinition Moment(string key, Figure f, string title, string text, params (string Label, string Description, EventEffects Effects)[] options)
    {
        var def = new EventDefinition { Id = key, Title = title, Text = text, Figure = f.Name, DefaultOption = options.Length - 1 };
        foreach (var (label, description, effects) in options)
        {
            if (effects.Figure.Length == 0 && (effects.FigurePrestige != 0 || effects.FigureAmbition != 0 || effects.FigureRisk != 0)) effects.Figure = f.Name;
            def.Options.Add(new EventOption { Label = label, Description = description, Effects = effects });
        }
        return def;
    }
}
