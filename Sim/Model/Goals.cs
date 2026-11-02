namespace Politicom.Sim;

public enum GoalKind { Hold, LowerDrift, EndHunger, Trust, Education, Radicalize }

/// <summary>
/// A goal the movement can take on in the long middle of a game: hold a threatened town through
/// the winter, bring drift down by spring, end a hunger, raise trust, deepen political education,
/// stir the enemy's workers. Offered as a decision; taken on, it has a deadline, a reward if met
/// and a small cost in enthusiasm if not.
/// </summary>
public sealed class Goal
{
    public string Id { get; set; } = "";
    public GoalKind Kind { get; set; }
    public string Title { get; set; } = "";
    /// <summary>The commune it concerns (Hold), or -1.</summary>
    public int Commune { get; set; } = -1;
    /// <summary>The level to reach: drift at most, or trust, education or radicalization at least.</summary>
    public double Target { get; set; }
    /// <summary>Where it stood when it was offered, for showing progress.</summary>
    public double Start { get; set; }
    public int Offered { get; set; }
    public int Deadline { get; set; }
    public bool Active { get; set; }
}
