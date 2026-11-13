namespace Politicom.Sim;

public enum BattleResult { Repulsed, Held, LostGround, Fell }

/// <summary>
/// One turn's fighting at one front commune, as WarSystem resolved it: the attack it faced and
/// the defence it put up (with their causes, from <see cref="Explain"/>), the ground it held or
/// lost, its works, and whether it fell. Kept for the last few months so the map can show how
/// each battle went.
/// </summary>
public sealed class BattleReport
{
    public int Tick { get; set; }
    public int Commune { get; set; }
    public int Enemy { get; set; }
    public string EnemyName { get; set; } = "";
    /// <summary>The enemy's offensive under way, if any.</summary>
    public string Offensive { get; set; } = "";
    /// <summary>Pressure on the commune, and its defence (a defensive campaign's help included).</summary>
    public double Attack { get; set; }
    public double Defense { get; set; }
    public double ControlBefore { get; set; }
    public double ControlAfter { get; set; }
    public double FortBefore { get; set; }
    public double FortAfter { get; set; }
    /// <summary>People under arms in the commune (thousands).</summary>
    public double Militia { get; set; }
    /// <summary>Help from a defensive campaign's columns.</summary>
    public double Campaign { get; set; }
    public BattleResult Result { get; set; }
    public List<Part> DefenseAdded { get; set; } = new();
    public List<Part> DefenseFactors { get; set; } = new();
    public List<Part> AttackAdded { get; set; } = new();
    public List<Part> AttackFactors { get; set; } = new();
}
