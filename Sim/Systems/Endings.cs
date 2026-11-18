namespace Politicom.Sim;

/// <summary>
/// How a finished game ends, in words: which ending it reached (from the outcome and how it was
/// won) and an epilogue on the figures, the communes and the ideas.
/// </summary>
public static class Endings
{
    public static (string Title, string Verdict) Of(World w)
    {
        var s = w.Scores;
        int sisters = w.States.Count(x => x.Sister);
        bool peace = w.Enemies.Count > 0 && w.Enemies.All(e => e.AtPeace);
        string when = w.Format(w.Date, "MMMM yyyy");
        return w.Outcome switch
        {
            Outcome.BecameState => ("You won the war and lost the revolution",
                "Hierarchy drift reached its final threshold. A state crystallized out of the councils, commissars and police."),
            Outcome.Survived when sisters >= 1 => ("The revolution spreads",
                $"Free communes still govern themselves in {when}, and {(sisters == 1 ? "another country has" : $"{sisters} other countries have")} risen and federated with them. Borders are beginning to mean less."),
            Outcome.Survived when peace => ("A recognized autonomy",
                $"Every government that fought the federation has made peace with it. In {when} the free communes stand, recognized by their enemies, their militias at home."),
            Outcome.Survived when w.Drift.Value >= 50 => ("The federation endured, in name",
                $"The communes survived to {when}, but commissars and permanent councils run much of what the assemblies once decided. It is a federation in name; whether it is free is another question."),
            Outcome.Survived when s.Freedom >= 75 && s.QualityOfLife >= 50 => ("A free society",
                $"In {when} the communes are fed, educated and still their own masters. The assemblies decide; delegates are recalled; nobody rules."),
            Outcome.Survived => ("The federation endured", $"Free communes still governed themselves in {when}."),
            _ when w.Communes.Count(c => !c.Occupied && !c.Federated) > w.Communes.Count(c => c.Occupied) =>
                ("The federation has dissolved", "Most communes went their own way. They are still free, but there is no federation left to hold together."),
            _ when s.Survival >= 60 || s.Spread >= 10 => ("Crushed, but remembered",
                "The federation was destroyed, but it held out long enough to be remembered, and its ideas have gone further than its militias ever did. Workers abroad tell its story, and some of them are already organizing."),
            _ => ("The federation has been destroyed", "Too few free communes remain for a federation."),
        };
    }

    /// <summary>A few lines on what became of the figures, the communes and the movement's ideas.</summary>
    public static IEnumerable<string> Epilogue(World w)
    {
        foreach (var f in w.Figures)
        {
            string role = Figure.RoleName(f.Role);
            yield return !f.Alive ? $"{f.Name}, {role}, did not live to see the end."
                : f.Retired ? $"{f.Name}, {role}, had long since retired, and lived to see it."
                : f.Empowered ? $"{f.Name}, {role}, took the power they asked for, and kept it."
                : f.Hunger >= 0.3 ? $"{f.Name}, {role}, ended the war with a following and an appetite for office."
                : f.Prestige >= 0.6 ? $"{f.Name}, {role}, is honoured across the federation, and still answers to the assemblies."
                : $"{f.Name}, {role}, went back to their work.";
        }
        int free = w.Federation.Count(), lost = w.Communes.Count(c => c.Occupied);
        yield return $"{free} of {w.InitialCommuneCount} communes ended free and federated" + (lost > 0 ? $"; {lost} fell to the enemy." : ".");
        var fortified = w.Communes.Where(c => c.Fortification > 0.5 && !c.Occupied).Select(c => c.Name).Take(3).ToList();
        if (fortified.Count > 0) yield return $"The earthworks at {string.Join(", ", fortified)} held.";
        double culture = w.Federation.Any() ? w.Federation.Average(c => c.Culture) : 0;
        if (culture > 0.3) yield return "Its schools, presses and songs outlived the war.";
        int sisters = w.States.Count(x => x.Sister);
        if (sisters > 0) yield return $"{sisters} sister {(sisters == 1 ? "federation" : "federations")} rose abroad.";
        // Its legacies: the chapters already closed, and what this last one leaves (two milestones or more on a path).
        foreach (var line in w.Legacy.History) yield return line + ".";
        var last = Systems.LegacySystem.Paths.Where(p => w.Legacy.Count(p) >= 2)
            .Select(p => $"{Systems.LegacySystem.LegacyName(p).ToLowerInvariant()} ({Systems.LegacySystem.Name(p).ToLowerInvariant()}{(w.Legacy.Count(p) == 3 ? ", all three milestones" : "")})").ToList();
        yield return last.Count > 0
            ? $"{(w.Legacy.History.Count > 0 ? "Its last chapter leaves" : "It leaves")} {(last.Count == 1 ? "a legacy" : "legacies")}: {string.Join("; ", last)}."
            : "No path reached two milestones: it leaves no legacy of its own.";
    }
}
