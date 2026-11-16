namespace Politicom.Sim.Systems;

/// <summary>
/// The figures act in character. Every couple of months one of them does something the movement
/// must answer: a commander wants to strike back from a front that holds, or refuses to give
/// ground at one that doesn't; an orator offers to speak to a discontented town, or to denounce
/// the party; an organizer proposes requisitions for a hungry commune, or double shifts for the
/// front; an educator wants a newspaper, or writes against an ambitious comrade; two figures
/// quarrel. What the movement answers changes the war, the communes and the figures themselves:
/// backing a figure feeds their prestige and their ambition, and they remember it, as refusing
/// them is remembered too. Their traits make some moments likelier (a brave commander strikes
/// back, a zealot denounces the party), rivals quarrel, and a bitter figure may threaten to
/// break with the movement.
/// </summary>
internal static class FigureMoments
{
    public static void Run(World w)
    {
        var b = w.Balance;
        if (w.Tick < 4 || (w.Tick - w.LastFigureMoment) * w.Weeks < b.FigureMomentGap) return;
        if (!w.Rng.Chance(b.FigureMomentRate * w.Weeks)) return;
        if (w.PendingEvents.Any(p => p.Id.StartsWith("figure_"))) return;
        var options = Candidates(w).Where(m => !Recent(w, m.Moment.Id)).ToList();
        if (options.Count == 0) return;
        var moment = w.Rng.PickWeighted(options.Select(o => o.Moment).ToList(), options.Select(o => o.Weight).ToList());
        moment.Id = $"figure_moment_{moment.Id}_{w.Tick}";
        moment.Tick = w.Tick + 1;
        moment.ExpiresAfter = 4;
        w.Scenario.Events.Add(moment);
        w.LastFigureMoment = w.Tick;
    }

    /// <summary>The same figure doing the same thing again within a year would be one too many.</summary>
    private static bool Recent(World w, string key) =>
        w.Scenario.Events.Any(e => e.Id.StartsWith($"figure_moment_{key}_") && (w.Tick - e.Tick) * w.Weeks < 52);

    /// <summary>A refusal stings a zealot twice as much, a pragmatist half as much.</summary>
    private static double Sting(Figure f, double regard) =>
        regard >= 0 ? regard : regard * (f.Has(FigureTrait.Zealous) ? 2 : 1) * (f.Has(FigureTrait.Pragmatic) ? 0.5 : 1) * (f.Has(FigureTrait.Vain) ? 1.5 : 1);

    private static IEnumerable<(EventDefinition Moment, double Weight)> Candidates(World w)
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
                    yield return (Moment($"strike_{f.Id}", f, $"{f.Name} wants to strike back",
                        $"The line at {c.Name} is holding, and {f.Name} wants to take the fight to the enemy: a night attack on its forward positions. " +
                        "The column is eager. It will cost lives, and a commander who wins it will be talked about.",
                        ("Let them attack", "A blow to the enemy, and to its morale. The commander's name grows.",
                            new EventEffects { EnemyStrength = -blow, EnemyExhaustion = 0.05, FigurePrestige = 0.12, FigureAmbition = 0.05, FigureRisk = f.Has(FigureTrait.Brave) ? 0.12 : f.Has(FigureTrait.Cautious) ? 0.04 : 0.08,
                                Communes = new[] { c.Name }, Population = -0.005, FigureRegard = 0.1, Memory = $"was let loose on the enemy at {c.Name}" }),
                        ("Hold the line", "No risks. The column grumbles that the assemblies are timid.",
                            new EventEffects { FigurePrestige = -0.03, Communes = new[] { c.Name }, Wellbeing = -0.02,
                                FigureRegard = Sting(f, f.Has(FigureTrait.Brave) ? -0.15 : f.Has(FigureTrait.Cautious) ? 0 : -0.08), Memory = $"was held back at {c.Name}" })),
                        f.Has(FigureTrait.Brave) ? 2.5 : f.Has(FigureTrait.Cautious) ? 0.4 : 1);
                    break;
                case FigureRole.Commander when c.Front && c.Pressure > c.Defense:
                    yield return (Moment($"stand_{f.Id}", f, $"{f.Name} will not fall back",
                        $"{c.Name} is giving way. The defence committee wants an orderly retreat to the next line; {f.Name} refuses, and says the column will hold the town street by street.",
                        ("Stand and fight", "Ground held for now, at a cost in lives; the commander is in the thick of it.",
                            new EventEffects { Control = 0.1, Communes = new[] { c.Name }, Population = -0.01, Enthusiasm = 0.03, FigurePrestige = 0.1, FigureAmbition = 0.04,
                                FigureRisk = f.Has(FigureTrait.Brave) ? 0.15 : f.Has(FigureTrait.Cautious) ? 0.05 : 0.1, FigureRegard = 0.1, Memory = $"was trusted to hold {c.Name}" }),
                        ("Fall back in good order", "The column lives to fight again; the town gives ground.",
                            new EventEffects { Control = -0.06, Communes = new[] { c.Name }, FigurePrestige = -0.05,
                                FigureRegard = Sting(f, f.Has(FigureTrait.Brave) ? -0.15 : f.Has(FigureTrait.Cautious) ? 0.02 : -0.08), Memory = $"was ordered to retreat from {c.Name}" })),
                        f.Has(FigureTrait.Brave) ? 2.5 : f.Has(FigureTrait.Cautious) ? 0.3 : 1);
                    break;
                case FigureRole.Orator:
                    if (w.Federation.Where(d => d.Discontent > 0.4).OrderByDescending(d => d.Discontent).FirstOrDefault() is { } unhappy)
                        yield return (Moment($"speak_{f.Id}", f, $"{f.Name} offers to speak in {unhappy.Name}",
                            $"{unhappy.Name} is restless: its assembly talks of going its own way. {f.Name} offers to go and speak to its assembly.",
                            ("Send them", "Hearts are won back, for a while; and the crowd's love goes to the speaker.",
                                new EventEffects { Communes = new[] { unhappy.Name }, Trust = f.Has(FigureTrait.Eloquent) ? 0.09 : 0.06, Wellbeing = 0.05, Influence = -4, FigurePrestige = 0.08, FigureAmbition = 0.05,
                                    FigureRegard = 0.08, Memory = $"was sent to win back {unhappy.Name}" }),
                            ("Let the assembly settle it", "Slower, and nobody's triumph. Its people learn to argue it out.",
                                new EventEffects { Communes = new[] { unhappy.Name }, PoliticalEducation = 0.02, FigureRegard = Sting(f, -0.04), Memory = $"was not sent to {unhappy.Name}" })),
                            f.Has(FigureTrait.Eloquent) ? 2 : 1);
                    if (w.Federation.Any() && w.Federation.Average(d => d.PartyInfluence) > 0.2)
                        yield return (Moment($"denounce_{f.Id}", f, $"{f.Name} denounces the party",
                            $"{f.Name} wants to speak out against the party's cells: their commissars, their lists, their meetings behind closed doors. Others plead for unity while the war lasts.",
                            ("Let them speak", "The cells lose ground everywhere; the party and its patrons are furious.",
                                new EventEffects { PartyInfluence = -0.15, FigurePrestige = 0.1, FigureAmbition = 0.03, Enthusiasm = -0.02, FigureRegard = 0.12, Memory = "was let speak against the party" }),
                            ("Keep the unity", "No public quarrel. The cells go on growing quietly.",
                                new EventEffects { PartyInfluence = 0.04, Enthusiasm = 0.02, FigureRegard = Sting(f, -0.1), Memory = "was silenced for unity's sake" })),
                            f.Has(FigureTrait.Zealous) ? 3 : f.Has(FigureTrait.Pragmatic) ? 0.3 : 1);
                    break;
                case FigureRole.Organizer:
                    if (w.Federation.FirstOrDefault(d => d.Starving) is { } hungry)
                        yield return (Moment($"requisition_{f.Id}", f, $"{f.Name} proposes requisitions for {hungry.Name}",
                            $"{hungry.Name} is going hungry. {f.Name} proposes that a food committee requisition grain from the better-off communes and send it there, quickly.",
                            ("Requisition", "Grain arrives at once, and committees learn to take: drift. The organizer's hand grows stronger.",
                                new EventEffects { Communes = new[] { hungry.Name }, Food = 6, Drift = 1.5, FigurePrestige = 0.08, FigureAmbition = 0.08, FigureRegard = 0.08, Memory = $"ran the requisitions for {hungry.Name}" }),
                            ("Appeal for voluntary sharing", "Less, and slower, but given freely.",
                                new EventEffects { Communes = new[] { hungry.Name }, Food = 3, Trust = 0.02, Influence = -5, FigureRegard = Sting(f, -0.06), Memory = "was overruled on the requisitions" })),
                            f.Has(FigureTrait.Pragmatic) || f.Has(FigureTrait.Ambitious) ? 2 : 1);
                    if (w.Federation.Any(d => d.Front))
                        yield return (Moment($"shifts_{f.Id}", f, $"{f.Name} calls for double shifts",
                            $"The front is short of everything. {f.Name} asks the workshops to work double shifts for the militias until the crisis passes.",
                            ("Double shifts", "Arms and supplies for the front; tired, unhappy workers.",
                                new EventEffects { GoodsToFront = 40 + 6 * w.Federation.Count(), Wellbeing = -0.04, FigurePrestige = 0.06, FigureAmbition = 0.05, FigureRegard = 0.06, Memory = "got the double shifts" }),
                            ("Keep the eight-hour day", "The workshops keep their hours; the movement keeps its promise.",
                                new EventEffects { Enthusiasm = 0.02, FigurePrestige = -0.02, FigureRegard = Sting(f, -0.05), Memory = "was refused the double shifts" })),
                            f.Has(FigureTrait.Pragmatic) ? 1.5 : 1);
                    break;
                case FigureRole.Educator:
                    var rival = atWork.Where(o => o != f && o.Hunger >= 0.25).OrderByDescending(o => o.Hunger).FirstOrDefault();
                    if (rival != null)
                        yield return (Moment($"pamphlet_{f.Id}_{rival.Id}", f, $"{f.Name} writes against {rival.Name}",
                            $"{f.Name} has written a pamphlet against {rival.Name}: the {Figure.RoleName(rival.Role)} is gathering a following, it says, and a following is how a revolution grows a master.",
                            ("Print it", $"{rival.Name}'s standing falls, and so does drift; their followers are bitter.",
                                new EventEffects { Rival = rival.Name, RivalPrestige = -0.15, RivalAmbition = -0.12, Drift = -2, FigurePrestige = 0.05,
                                    // Their followers, where they work, take it badly; and so does the rival.
                                    Communes = new[] { w.Communes[rival.Commune!.Value].Name }, Wellbeing = -0.03,
                                    FigureRegard = 0.08, Memory = $"saw their pamphlet against {rival.Name} printed",
                                    RivalRegard = Sting(rival, -0.2), RivalMemory = $"was attacked in print by {f.Name}, with the movement's blessing", Relation = -0.4 }),
                            ("Ask them to hold it", "No quarrel in public. Nothing checks the rival.",
                                new EventEffects { FigurePrestige = -0.02, FigureRegard = Sting(f, -0.08), Memory = $"was asked to hold their pamphlet against {rival.Name}" })),
                            f.Has(FigureTrait.Zealous) || f.Has(FigureTrait.Honest) ? 2 : 1 + (f.RivalOf(rival.Id) ? 1 : 0));
                    else
                        yield return (Moment($"paper_{f.Id}", f, $"{f.Name} wants to found a newspaper",
                            $"{f.Name} wants to found a newspaper in {c.Name}: news from the front, debates from the assemblies, poems, a page for the children.",
                            ("Fund it", "Political education and culture rise around it, and so does its editor's name.",
                                new EventEffects { Communes = new[] { c.Name }, Influence = -6, PoliticalEducation = f.Has(FigureTrait.Eloquent) ? 0.07 : 0.05, Culture = 0.08, FigurePrestige = 0.05, FigureAmbition = 0.02,
                                    FigureRegard = 0.08, Memory = $"founded a newspaper in {c.Name}" }),
                            ("Not now", "Paper and ink go to the front.", new EventEffects { FigureRegard = Sting(f, -0.03), Memory = "was refused a newspaper" })),
                            1);
                    break;
            }
        }

        // Two figures fall out: rivals first, else the two most prestigious.
        var pair = atWork.SelectMany(x => atWork.Where(y => y.Id > x.Id).Select(y => (x, y)))
            .Where(p => p.x.RivalOf(p.y.Id) || p.y.RivalOf(p.x.Id)).OrderBy(p => p.x.OpinionOf(p.y.Id)).FirstOrDefault();
        bool rivals = pair.x != null;
        if (!rivals)
        {
            var proud = atWork.Where(f => f.Prestige >= 0.4).OrderByDescending(f => f.Prestige).Take(2).ToList();
            if (proud.Count == 2) pair = (proud[0], proud[1]);
        }
        if (pair.x != null)
        {
            var (x, y) = pair;
            yield return (Moment($"quarrel_{x.Id}_{y.Id}", x, $"{x.Name} and {y.Name} fall out",
                (rivals ? $"The old rivalry between {x.Name} and {y.Name} has boiled over: they" : $"{x.Name} and {y.Name}") +
                " are quarrelling in public over who speaks for the movement, and their followers are taking sides.",
                ($"Side with {x.Name}", $"{y.Name} loses standing; their followers sulk.",
                    new EventEffects { FigurePrestige = 0.05, Rival = y.Name, RivalPrestige = -0.12, Trust = -0.02, Relation = -0.3,
                        FigureRegard = 0.15, Memory = $"was backed against {y.Name}", RivalRegard = Sting(y, -0.2), RivalMemory = $"lost the quarrel with {x.Name}" }),
                ($"Side with {y.Name}", $"{x.Name} loses standing; their followers sulk.",
                    new EventEffects { FigurePrestige = -0.12, Rival = y.Name, RivalPrestige = 0.05, Trust = -0.02, Relation = -0.3,
                        FigureRegard = Sting(x, -0.2), Memory = $"lost the quarrel with {y.Name}", RivalRegard = 0.15, RivalMemory = $"was backed against {x.Name}" }),
                ("Put it to the congress", "The delegates decide, not the famous: both lose a little of their shine.",
                    new EventEffects { Influence = -5, FigurePrestige = -0.04, FigureAmbition = -0.05, Rival = y.Name, RivalPrestige = -0.04, RivalAmbition = -0.05, PoliticalEducation = 0.01, Relation = -0.1,
                        FigureRegard = Sting(x, -0.03), RivalRegard = Sting(y, -0.03) })),
                rivals ? 3 : 0.6);
        }

        // A bitter figure threatens to break with the movement.
        foreach (var f in atWork.Where(f => f.Regard <= -0.45))
        {
            var last = f.Memories.LastOrDefault(m => m.Regard < 0);
            yield return (Moment($"bitter_{f.Id}", f, $"{f.Name} is bitter",
                $"{f.Name} has had enough{(last != null ? $": they still speak of how they {last.Text}" : "")}. They talk of breaking with the movement and taking their following with them.",
                ("Make amends", "A meeting, an apology, a post of trust: they come back, for now.",
                    new EventEffects { Influence = -8, FigureRegard = 0.45, Memory = "was offered amends", FigureAmbition = 0.03 }),
                ("Rotate them before they split the movement", "Back to the ranks: their following is angry, but the split is headed off.",
                    new EventEffects { Figure = f.Name, FigureChoice = FigureChoice.Rotate, Enthusiasm = -0.02 }),
                ("Let them speak their mind", "They denounce the leadership in public. The movement is shaken; some think them right.",
                    new EventEffects { Enthusiasm = -0.05, Trust = -0.02, Drift = -1, FigureRegard = 0.2, FigurePrestige = 0.05, Memory = "said their piece in public" })),
                4);
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
