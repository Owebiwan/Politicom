using Politicom.Sim;

namespace Politicom.Cli;

/// <summary>The scenario, playable in a terminal.</summary>
internal static class Play
{
    private const string DefaultSave = "politicom-save.json";

    private const string Help = """
        Communes decide for themselves. You can only influence the conditions they decide in.

          list                      all communes            show <commune>       one commune in detail
          techs                     technologies and spread  log [n]             recent events

          school <commune> practical|political   fund schooling (costs labor now, capacity later)
          pamphlets <commune>                     political education here and next door
          mediate <a> <b>                         raise trust between two neighbours
          project famine <commune>                famine relief for a starving commune
          project railway <a> <b>                 rail line (trust, cheaper sharing, some drift)
          project defense                         defensive campaign for the front
          project ateneo <commune>                ateneo network around a commune
          project collectivize                    plan industry together (output, drift)
          persuade <commune> <project#>           make the case in an assembly
          congress                                spread techniques federation-wide (some drift)
          idea <tech_id>                          circulate a technology for 20 turns
          recall                                  recall delegates: drift down, experience lost
          committee on|off                        war committee: defense up, drift up every turn
          trade on|off                            sell surplus grain abroad for arms (drift; embargoes)
          arms accept|reject                      answer the government's offer
          hold <commune>                          keep a seceding commune by force (costly)
          choose <n>                              answer the oldest historical event with option n

          save [file] / load [file] save or restore the game (default politicom-save.json)
          next [n] / Enter          advance n turns          quit
        <commune> is a number or the start of a name.
        """;

    public static int Run(Options o)
    {
        string dataDir = DataPaths.Find();
        var w = o.Get("load") is { } savePath
            ? World.LoadJson(File.ReadAllText(savePath), TechCatalog.Load(DataPaths.Technologies(dataDir)))
            : World.Load(dataDir, o.Scenario, o.BuildBalance(), o.GetSeed());
        Console.WriteLine(Render.Bold(w.Scenario.Name));
        Console.WriteLine(w.Scenario.Description);
        Console.WriteLine(Render.Dim("\nType `help` for commands.\n"));
        Console.WriteLine(Render.CommuneTable(w));
        int shown = w.Log.Count;
        Console.WriteLine(Render.Status(w));

        while (true)
        {
            if (w.Outcome != Outcome.Ongoing)
            {
                PrintNewEvents(w, ref shown);
                Console.WriteLine(Render.Status(w));
                Console.WriteLine(Render.Bold($"Game over: {w.Outcome}. Final score {w.Scores.Total:0}."));
                return 0;
            }

            Console.Write(Render.Paint("› ", "36"));
            string? line = Console.ReadLine();
            if (line is null) return 0;
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts.Length == 0 ? "next" : parts[0].ToLowerInvariant();
            try
            {
                switch (cmd)
                {
                    case "quit" or "exit" or "q":
                        return 0;
                    case "help" or "?":
                        Console.WriteLine(Help);
                        break;
                    case "list" or "ls":
                        Console.WriteLine(Render.CommuneTable(w));
                        break;
                    case "show":
                        Console.WriteLine(Render.CommuneDetail(w, w.Communes[Commune(w, parts, 1)]));
                        break;
                    case "techs":
                        Console.WriteLine(Render.TechTable(w));
                        break;
                    case "log":
                        int n = parts.Length > 1 ? int.Parse(parts[1]) : 15;
                        foreach (var e in w.Log.TakeLast(n)) Console.WriteLine(Render.Event(e));
                        break;
                    case "save":
                        string saveTo = parts.Length > 1 ? parts[1] : DefaultSave;
                        File.WriteAllText(saveTo, w.SaveJson());
                        Console.WriteLine(Render.Paint($"Saved {w.Turn} {w.Tick} to {saveTo}.", "32"));
                        break;
                    case "load":
                        string loadFrom = parts.Length > 1 ? parts[1] : DefaultSave;
                        w = World.LoadJson(File.ReadAllText(loadFrom), w.Techs);
                        shown = w.Log.Count;
                        Console.WriteLine(Render.Paint($"Loaded {loadFrom}.", "32"));
                        Console.WriteLine(Render.Status(w));
                        break;
                    case "next" or "n":
                        int weeks = parts.Length > 1 ? int.Parse(parts[1]) : 1;
                        for (int i = 0; i < weeks && w.Outcome == Outcome.Ongoing; i++)
                        {
                            w.Step();
                            // Like a Paradox popup: stop fast-forwarding when a decision is needed.
                            if (w.Log.Skip(shown).Any(e => e.StopsTheClock)) break;
                        }
                        PrintNewEvents(w, ref shown);
                        if (w.Outcome == Outcome.Ongoing) Console.WriteLine(Render.Status(w)); // the game-over block prints it otherwise
                        break;
                    default:
                        var action = ParseAction(w, cmd, parts);
                        if (action is null)
                        {
                            Console.WriteLine($"Unknown command '{cmd}'. Type `help`.");
                            break;
                        }
                        var result = w.Apply(action);
                        Console.WriteLine(result.Ok ? Render.Paint(result.Message, "32") : Render.Paint(result.Message, "31"));
                        shown = w.Log.Count;
                        break;
                }
            }
            catch (Exception e) when (e is FormatException or ArgumentException or IndexOutOfRangeException or IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                Console.WriteLine(Render.Paint(e.Message, "31"));
            }
        }
    }

    private static PlayerAction? ParseAction(World w, string cmd, string[] p) => cmd switch
    {
        "school" => new FundSchool(Commune(w, p, 1), Arg(p, 2).StartsWith("pol") ? EducationTrack.Political : Arg(p, 2).StartsWith("prac") ? EducationTrack.Practical
            : throw new ArgumentException("Track must be practical or political.")),
        "pamphlets" => new CirculatePamphlets(Commune(w, p, 1)),
        "mediate" => new Mediate(Commune(w, p, 1), Commune(w, p, 2)),
        "project" => Arg(p, 1) switch
        {
            "famine" => new ProposeProject(ProjectKind.FamineRelief, Commune(w, p, 2)),
            "railway" => new ProposeProject(ProjectKind.Railway, Commune(w, p, 2), Commune(w, p, 3)),
            "defense" or "defence" => new ProposeProject(ProjectKind.DefensiveCampaign),
            "ateneo" => new ProposeProject(ProjectKind.AteneoNetwork, Commune(w, p, 2)),
            "collectivize" => new ProposeProject(ProjectKind.Collectivization),
            _ => throw new ArgumentException("Project must be famine, railway, defense, ateneo or collectivize."),
        },
        "persuade" => new Persuade(Commune(w, p, 1), int.Parse(Arg(p, 2).TrimStart('#'))),
        "congress" => new SponsorCongress(),
        "idea" => new CirculateIdea(Arg(p, 1)),
        "recall" => new RecallDelegates(),
        "committee" => new SetWarCommittee(Arg(p, 1) == "on"),
        "trade" => new SetForeignTrade(Arg(p, 1) == "on"),
        "arms" => new AnswerAllyOffer(Arg(p, 1) == "accept"),
        "hold" => new HoldCommune(Commune(w, p, 1)),
        "choose" => w.PendingEvents.Count == 0
            ? throw new ArgumentException("No event is waiting for an answer.")
            : new ChooseEventOption(w.PendingEvents[0].Id, int.Parse(Arg(p, 1)) - 1),
        _ => null,
    };

    private static void PrintNewEvents(World w, ref int shown)
    {
        for (; shown < w.Log.Count; shown++) Console.WriteLine(Render.Event(w.Log[shown]));
    }

    private static string Arg(string[] p, int i) =>
        i < p.Length ? p[i].ToLowerInvariant() : throw new ArgumentException("Missing argument. Type `help`.");

    private static int Commune(World w, string[] p, int i)
    {
        string s = Arg(p, i);
        if (int.TryParse(s, out int id) && id >= 0 && id < w.Communes.Count) return id;
        var match = w.Communes.Where(c => c.Name.StartsWith(s, StringComparison.OrdinalIgnoreCase)).ToList();
        if (match.Count == 1) return match[0].Id;
        throw new ArgumentException(match.Count == 0 ? $"No commune '{s}'." : $"'{s}' is ambiguous: {string.Join(", ", match.Select(c => $"{c.Name} [{c.Id}]"))}.");
    }
}
