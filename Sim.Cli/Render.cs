using System.Text;
using Politicom.Sim;

namespace Politicom.Cli;

/// <summary>Terminal views shared by `play` and `run`.</summary>
internal static class Render
{
    public static bool Color { get; set; } = !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;

    public static string Paint(string text, string code) => Color ? $"\u001b[{code}m{text}\u001b[0m" : text;
    public static string Dim(string t) => Paint(t, "2");
    public static string Bold(string t) => Paint(t, "1");

    public static string Event(GameEvent e)
    {
        string tag = e.Severity switch
        {
            Severity.Good => Paint("+", "32"),
            Severity.Warning => Paint("!", "33"),
            Severity.Danger => Paint("✖", "31"),
            Severity.Decision => Paint("?", "36;1"),
            _ => Dim("·"),
        };
        return $"{Dim(e.Date.ToString("dd MMM yyyy"))} {tag} {e.Text}";
    }

    public static string Bar(double value, double max, int width, string code)
    {
        int filled = (int)Math.Round(Math.Clamp(value / max, 0, 1) * width);
        return Paint(new string('█', filled), code) + Dim(new string('░', width - filled));
    }

    public static string Status(World w)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine(Bold($"══ {w.Scenario.Name} · {w.Date:dd MMMM yyyy} · {w.Turn} {w.Tick}/{w.Scenario.EndTick} ══"));
        sb.AppendLine($"Influence {Bold($"{w.Influence:0}")} (+{w.InfluenceRegen:0.0}/{w.Turn})   Federation {w.Federation.Count()}/{w.InitialCommuneCount}   " +
                      $"Occupied {w.Communes.Count(c => c.Occupied)}   Seceded {w.Communes.Count(c => !c.Occupied && !c.Federated)}" +
                      (w.WarCommittee ? Paint("   WAR COMMITTEE SITTING", "33") : "") +
                      (w.Ally.MilitarizationTicks > 0 ? Paint($"   militarized {w.Turns(w.Ally.MilitarizationTicks)}", "33") : ""));

        string driftColor = w.Drift.Stage switch { DriftStage.Free => "32", DriftStage.PermanentCouncils => "33", _ => "31" };
        sb.AppendLine($"Drift     {Bar(w.Drift.Value, 100, 40, driftColor)} {w.Drift.Value,5:0.0}  {Bold(DriftEffects.Name(w.Drift.Stage))}" +
                      (w.Drift.NextThreshold is { } t ? Dim($"  next stage at {t:0}") : ""));
        var d = w.LastDrift;
        var top = d.Items().Where(i => Math.Abs(i.Value) > 0.005).OrderByDescending(i => Math.Abs(i.Value)).Take(4)
            .Select(i => $"{i.Label} {(i.Value >= 0 ? Paint($"+{i.Value:0.00}", "31") : Paint($"{i.Value:0.00}", "32"))}");
        sb.AppendLine(Dim($"          last {w.Turn} ") + $"{(d.Net >= 0 ? "+" : "")}{d.Net:0.00}: " + string.Join(", ", top));

        foreach (var e in w.Enemies)
            sb.AppendLine($"Enemy     {e.Name} strength {e.EffectiveStrength:0.0}" + (e.CurrentOffensive is { } o ? Paint($"  ▶ {o.Name.ToUpperInvariant()}", "31;1") : "") +
                          Dim($"   exhaustion {e.Exhaustion:P0}   workers radicalized {e.Radicalization:P0}"));
        var fronts = w.Active.Where(c => c.Front).ToList();
        if (fronts.Count > 0)
            sb.AppendLine("Front     " + string.Join("  ", fronts.Select(c =>
                $"{c.Name} {(c.Defense >= c.Pressure ? Paint($"{c.Control:P0}", "32") : Paint($"{c.Control:P0}", "31"))}")));

        var s = w.Scores;
        sb.AppendLine($"Scores    survival {s.Survival:0}  quality {s.QualityOfLife:0}  spread {s.Spread:0}  freedom {s.Freedom:0}" + Dim($"   (total {s.Total:0})"));

        foreach (var p in w.ActiveProjects)
            sb.AppendLine($"Project   #{p.Id} {p.Definition.Name}{Target(w, p)}  {Bar(p.Progress, p.Work, 20, "36")} {p.Progress:0}/{p.Work:0}  " +
                          $"{p.Participants.Count}/{p.Invited.Count} joined, {p.LastOverhead:P0} lost to coordination, due {w.Turn} {p.Deadline}");

        foreach (var pending in w.PendingEvents)
        {
            var def = w.EventDefinition(pending.Id);
            sb.AppendLine(Paint($"DECISION  {def.Title} — answer with `choose <n>` by {w.Turn} {pending.Expires} (otherwise option {def.DefaultOption + 1})", "36;1"));
            for (int i = 0; i < def.Options.Count; i++)
                sb.AppendLine($"          {i + 1}. {Bold(def.Options[i].Label)}{Dim($" — {def.Options[i].Effects.Summary(w.Turn)}")}");
        }
        if (w.Ally.OfferPending)
            sb.AppendLine(Paint($"DECISION  The {w.Ally.Name} offers arms for militarization. → `arms accept` / `arms reject` (expires {w.Turn} {w.Ally.OfferExpires})", "36;1"));
        foreach (var sec in w.PendingSecessions)
            sb.AppendLine(Paint($"DECISION  {w.Communes[sec.Commune].Name} [{sec.Commune}] will leave in {w.Turn} {sec.Deadline}. → `mediate {sec.Commune} <neighbour>` or `hold {sec.Commune}`", "36;1"));
        return sb.ToString();
    }

    public static string Target(World w, Project p) =>
        p.Target is { } a ? $" ({w.Communes[a].Name}" + (p.Target2 is { } b ? $" ↔ {w.Communes[b].Name}" : "") + ")" : "";

    public static string CommuneTable(World w)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Dim($"{"#",3} {"commune",-32} {"kind",-10} {"pop",4} {"food",5} {"goods",5} {"well",5} {"prac",4} {"pol",4} {"trust",5} {"disc",4}  status"));
        foreach (var c in w.Communes)
        {
            string status = c.Occupied ? Paint("occupied", "31")
                : !c.Federated ? Paint("seceded", "33")
                : string.Join(" ", new[]
                {
                    c.Front ? Paint($"front {c.Control:P0}", "31") : null,
                    c.Starving ? Paint("starving", "31") : null,
                    c.GoodsShort ? Paint("short", "33") : null,
                    c.FreeRiding > 0.2 ? Paint("free-riding", "33") : null,
                    c.ExpertGap > 0.1 ? Paint("experts", "35") : null,
                    w.PendingSecessions.Any(s => s.Commune == c.Id) ? Paint("LEAVING", "36;1") : null,
                }.Where(x => x != null));
            sb.AppendLine($"{c.Id,3} {Trunc(c.Name, 32),-32} {c.Kind,-10} {c.Population,4:0} {c.Food,5:0} {c.Goods,5:0} {c.Wellbeing,5:P0} {c.PracticalEducation,4:0.00} {c.PoliticalEducation,4:0.00} " +
                          $"{w.Trust.AverageTrust(c.Id),5:P0} {c.Discontent,4:0.00}  {status}");
        }
        return sb.ToString();
    }

    public static string CommuneDetail(World w, Commune c)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Bold($"[{c.Id}] {c.Name}") + Dim($" — {c.Kind}, {c.Population:0}k people") + (c.Description.Length > 0 ? $"\n    {c.Description}" : ""));
        sb.AppendLine($"    values      collectivism {c.Values.Collectivism:+0.0;-0.0}  militancy {c.Values.Militancy:+0.0;-0.0}  federalism {c.Values.Federalism:+0.0;-0.0}");
        sb.AppendLine($"    education   practical {c.PracticalEducation:0.00} (schooling {c.PracticalSchooling:P0})  political {c.PoliticalEducation:0.00} (schooling {c.PoliticalSchooling:P0})");
        sb.AppendLine($"    economy     food {c.Food:0} (+{c.FoodProduced:0.0}/{w.Turn})  goods {c.Goods:0} (+{c.GoodsProduced:0.0}/{w.Turn})  wellbeing {c.Wellbeing:P0}");
        sb.AppendLine($"    aid ledger  given {c.AidGiven:0.0}  received {c.AidReceived:0.0}  free-riding {c.FreeRiding:P0}  discontent {c.Discontent:0.00}");
        if (c.Front) sb.AppendLine($"    front       against {string.Join(", ", c.Fronts.Select(k => w.Enemies[k].Name))}: control {c.Control:P0}, defense {c.Defense:0.00} vs pressure {c.Pressure:0.00}");
        if (c.OccupiedBy is int by) sb.AppendLine($"    occupied    by the {w.Enemies[by].Name}");
        sb.AppendLine($"    techs       {(c.Techs.Count == 0 ? "none" : string.Join(", ", c.Techs.Select(t => w.Techs[t].Name)))}");
        sb.AppendLine("    neighbours  " + string.Join(", ", w.Trust.Neighbors(c.Id).Select(n =>
            $"{w.Communes[n].Name} [{n}] {TrustColor(w.Trust[c.Id, n])}")));
        return sb.ToString();
    }

    public static string TrustColor(double t) => Paint($"{t:P0}", t >= 0.6 ? "32" : t >= 0.4 ? "33" : "31");

    public static string TechTable(World w)
    {
        var sb = new StringBuilder();
        foreach (var t in w.Techs.All)
        {
            double share = w.TechShare(t.Id);
            string nudge = w.IdeaNudges.TryGetValue(t.Id, out var n) ? Paint($" circulating {w.Turns(n)}", "36") : "";
            sb.AppendLine($"{t.Id,-24} {Bar(share, 1, 10, "36")} {share,4:P0}  {Bold(t.Name)}{nudge}");
            sb.AppendLine(Dim($"{"",24}   + {t.Perk}.  − {t.Risk}") + (t.Effects.DriftPerTick > 0 ? Paint($" (drift +{t.Effects.DriftPerTick:0.00})", "31") : "") +
                          (t.Effects.DriftBrake > 0 ? Paint($" (brake −{t.Effects.DriftBrake:0.00})", "32") : ""));
        }
        return sb.ToString();
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
