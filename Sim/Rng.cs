namespace Politicom.Sim;

/// <summary>
/// PCG32. The simulation never touches System.Random so a seed replays identically
/// on every platform and .NET version — needed for batch balancing, saves and replays.
/// </summary>
public sealed class Rng
{
    private const ulong Multiplier = 6364136223846793005UL;
    private const ulong Increment = 1442695040888963407UL;
    private ulong _state;

    public Rng(ulong seed)
    {
        _state = 0;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    public ulong State
    {
        get => _state;
        set => _state = value;
    }

    public uint NextUInt()
    {
        ulong old = _state;
        _state = old * Multiplier + Increment;
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => NextUInt() / 4294967296.0;

    public bool Chance(double probability) => NextDouble() < probability;

    public int Next(int maxExclusive) => (int)(NextDouble() * maxExclusive);

    /// <summary>Uniform in [-amplitude, amplitude).</summary>
    public double Jitter(double amplitude) => (NextDouble() * 2 - 1) * amplitude;

    public T PickWeighted<T>(IReadOnlyList<T> items, IReadOnlyList<double> weights)
    {
        double total = 0;
        foreach (var w in weights) total += Math.Max(0, w);
        double roll = NextDouble() * total;
        for (int i = 0; i < items.Count; i++)
        {
            roll -= Math.Max(0, weights[i]);
            if (roll < 0) return items[i];
        }
        return items[^1];
    }
}
