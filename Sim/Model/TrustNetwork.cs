namespace Politicom.Sim;

/// <summary>
/// Weighted, undirected graph of trust between neighbouring communes (0..1 per edge).
/// High trust lets communes pool labor and share surplus; low trust means hoarding and disputes.
/// </summary>
public sealed class TrustNetwork
{
    private readonly double[,] _trust;
    private readonly List<int>[] _adjacent;

    public TrustNetwork(int count)
    {
        Count = count;
        _trust = new double[count, count];
        _adjacent = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
    }

    public int Count { get; }

    public double this[int a, int b] => _trust[a, b];

    public IReadOnlyList<int> Neighbors(int i) => _adjacent[i];

    public bool Connected(int a, int b) => _adjacent[a].Contains(b);

    public void Set(int a, int b, double value)
    {
        value = Math.Clamp(value, 0, 1);
        _trust[a, b] = value;
        _trust[b, a] = value;
    }

    public void Add(int a, int b, double delta)
    {
        if (a != b && Connected(a, b)) Set(a, b, _trust[a, b] + delta);
    }

    public void Link(int a, int b, double trust)
    {
        if (a == b || Connected(a, b)) return;
        _adjacent[a].Add(b);
        _adjacent[b].Add(a);
        Set(a, b, trust);
    }

    /// <summary>Neighbour lists in their exact order (iteration order matters for deterministic replays).</summary>
    internal List<int[]> ExportAdjacency() => _adjacent.Select(a => a.ToArray()).ToList();

    internal static TrustNetwork Import(List<int[]> adjacency, IEnumerable<TrustEdge> edges)
    {
        var net = new TrustNetwork(adjacency.Count);
        for (int i = 0; i < adjacency.Count; i++) net._adjacent[i].AddRange(adjacency[i]);
        foreach (var e in edges)
        {
            if (!net.Connected(e.A, e.B)) throw new InvalidDataException($"Save file has trust for unlinked communes {e.A} and {e.B}.");
            net.Set(e.A, e.B, e.Trust);
        }
        return net;
    }

    public IEnumerable<(int A, int B)> Edges()
    {
        for (int a = 0; a < Count; a++)
            foreach (var b in _adjacent[a])
                if (a < b) yield return (a, b);
    }

    public double AverageTrust(int i, Func<int, bool>? include = null)
    {
        double sum = 0;
        int n = 0;
        foreach (var j in _adjacent[i])
        {
            if (include != null && !include(j)) continue;
            sum += _trust[i, j];
            n++;
        }
        return n == 0 ? 0 : sum / n;
    }

    /// <summary>Breadth-first hop distance, or int.MaxValue if unreachable.</summary>
    public int Hops(int from, int to, Func<int, bool>? passable = null)
    {
        if (from == to) return 0;
        var dist = new Dictionary<int, int> { [from] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            foreach (var n in _adjacent[cur])
            {
                if (dist.ContainsKey(n) || (passable != null && n != to && !passable(n))) continue;
                dist[n] = dist[cur] + 1;
                if (n == to) return dist[n];
                queue.Enqueue(n);
            }
        }
        return int.MaxValue;
    }

    /// <summary>
    /// Connects each commune to its k nearest neighbours (plus anything within
    /// <paramref name="radiusKm"/>), then bridges any disconnected components.
    /// </summary>
    public static TrustNetwork Build(IReadOnlyList<Commune> communes, int k, double radiusKm, Func<Commune, Commune, double> initialTrust)
    {
        var net = new TrustNetwork(communes.Count);
        for (int i = 0; i < communes.Count; i++)
        {
            var nearest = Enumerable.Range(0, communes.Count).Where(j => j != i)
                .OrderBy(j => DistanceKm(communes[i], communes[j])).ToList();
            foreach (var j in nearest.Take(k)) net.Link(i, j, initialTrust(communes[i], communes[j]));
            foreach (var j in nearest.Where(j => DistanceKm(communes[i], communes[j]) <= radiusKm))
                net.Link(i, j, initialTrust(communes[i], communes[j]));
        }

        while (true)
        {
            var component = net.ComponentOf(0);
            if (component.Count == communes.Count) break;
            var (a, b) = (from x in component
                          from y in Enumerable.Range(0, communes.Count)
                          where !component.Contains(y)
                          orderby DistanceKm(communes[x], communes[y])
                          select (x, y)).First();
            net.Link(a, b, initialTrust(communes[a], communes[b]));
        }
        return net;
    }

    public static double DistanceKm(Commune a, Commune b)
    {
        double meanLat = (a.Latitude + b.Latitude) / 2 * Math.PI / 180;
        double dx = (a.Longitude - b.Longitude) * Math.Cos(meanLat) * 111.32;
        double dy = (a.Latitude - b.Latitude) * 110.57;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private HashSet<int> ComponentOf(int start)
    {
        var seen = new HashSet<int> { start };
        var stack = new Stack<int>();
        stack.Push(start);
        while (stack.Count > 0)
            foreach (var n in _adjacent[stack.Pop()])
                if (seen.Add(n)) stack.Push(n);
        return seen;
    }
}
