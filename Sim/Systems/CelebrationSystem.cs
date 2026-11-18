namespace Politicom.Sim.Systems;

/// <summary>
/// The movement marks what it has won. Every year on the day of the rising, the anniversary:
/// a celebration across the federation (influence and food, for enthusiasm, trust, culture and a
/// memory of what was won), a modest one, or none in hard times. When the last front closes, or
/// the allies recognize the collectives, liberation: a festival of the free communes. While a
/// celebration lasts the towns are dressed in flags and lanterns (CelebrationUntil).
/// </summary>
internal static class CelebrationSystem
{
    public static void Run(World w)
    {
        Anniversary(w);
        Liberation(w);
    }

    private static void Anniversary(World w)
    {
        var start = w.Scenario.StartDate;
        int years = w.Date.Year - start.Year;
        if (years < 1 || years <= w.AnniversariesMarked) return;
        // This turn reaches the day of the rising (the turn's week contains it, or has just passed it).
        var day = new DateOnly(w.Date.Year, start.Month, Math.Min(start.Day, DateTime.DaysInMonth(w.Date.Year, start.Month)));
        if (w.Date < day) return;
        w.AnniversariesMarked = years;
        if (w.Federation.Count() < 2) return;

        int free = w.Federation.Count(), lost = w.Communes.Count(c => c.Occupied);
        var fallen = w.Figures.Where(f => !f.Alive).Select(f => f.Name).ToList();
        string date = w.Format(day, "d MMMM");
        string[] words = { "", "a year", "two years", "three years", "four years", "five years", "six years", "seven years", "eight years", "nine years", "ten years" };
        string what = years < words.Length ? words[years] : $"{years} years";
        string held = lost == 0 ? $"All {free} communes are still free." : $"{free} communes are still free; {lost} have fallen.";
        string won = w.Struggle?.Recognized == true ? " The collectives are recognized." : w.Enemies.Count > 0 && w.Enemies.All(e => e.AtPeace) ? " The war is over." : "";
        string remembered = fallen.Count > 0 ? $" The speeches will name {string.Join(", ", fallen.Take(3))}." : "";
        bool hard = w.Federation.Any(c => c.Starving) || w.OffensiveUnderway;
        w.Scenario.Events.Add(new EventDefinition
        {
            Id = $"anniversary_{years}",
            Tick = w.Tick + 1,
            Title = $"{date}: {what} of the revolution",
            Text = $"It is {what} since the rising. {held}{won}{remembered} The assemblies ask how the anniversary should be marked."
                   + (hard ? " Times are hard: some say this is no year for feasting." : ""),
            DefaultOption = 1,
            ExpiresAfter = 3,
            Options =
            {
                new EventOption
                {
                    Label = "A celebration across the federation",
                    Description = "Delegations, songs, banners and long tables in every square. It costs food and organizers' time; the communes remember why they fight.",
                    Effects = new EventEffects { Influence = -10, Food = -2, Enthusiasm = 0.05, Trust = 0.015, Culture = 0.03, Celebrate = 2 },
                },
                new EventOption
                {
                    Label = "A modest celebration",
                    Description = "Speeches and a meal in each commune.",
                    Effects = new EventEffects { Influence = -4, Enthusiasm = 0.025, Culture = 0.01, Celebrate = 1 },
                },
                new EventOption
                {
                    Label = "No celebration this year",
                    Description = hard ? "The food goes to those who need it. People understand, mostly." : "The day passes like any other. People notice.",
                    Effects = new EventEffects { Enthusiasm = hard ? -0.01 : -0.03 },
                },
            },
        });
    }

    private static void Liberation(World w)
    {
        if (w.Liberated) return;
        bool peace = w.Enemies.Count > 0 && w.Enemies.All(e => e.AtPeace) && !w.Communes.Any(c => c.Front && c.InFederation);
        bool recognized = w.Struggle?.Recognized == true;
        if (!peace && !recognized) return;
        w.Liberated = true;
        string why = peace ? "The last front has closed. The militias are coming home, and for the first time since the rising no army stands against the communes."
                           : $"The {w.Struggle!.With} has recognized the collectives. What the assemblies built is theirs to keep.";
        w.Scenario.Events.Add(new EventDefinition
        {
            Id = "liberation",
            Tick = w.Tick + 1,
            Title = "Liberation",
            Text = $"{why} The communes want to celebrate it together.",
            DefaultOption = 0,
            ExpiresAfter = 3,
            Options =
            {
                new EventOption
                {
                    Label = "A festival of the free communes",
                    Description = "A week of music, theatre, dancing and long tables, and a congress to say what comes next.",
                    Effects = new EventEffects { Influence = -6, Enthusiasm = 0.15, Trust = 0.05, Culture = 0.06, Wellbeing = 0.05, Drift = -3, Celebrate = 3 },
                },
                new EventOption
                {
                    Label = "Back to work: there is much to rebuild",
                    Description = "A day off, a song, and back to the fields and the works.",
                    Effects = new EventEffects { Enthusiasm = 0.06, Wellbeing = 0.02, Celebrate = 1 },
                },
            },
        });
    }
}
