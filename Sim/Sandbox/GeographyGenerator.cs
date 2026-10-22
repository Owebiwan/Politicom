namespace Politicom.Sim.Sandbox;

/// <summary>
/// Map geography in lon/lat, in the same shape as the Data/geo files built from real map data:
/// land, lakes, rivers, land borders and provinces. Historical scenarios get theirs from
/// Data/geo; generated sandbox maps carry their own in the scenario.
/// </summary>
public sealed class GeographyDefinition
{
    /// <summary>lon_min, lat_min, lon_max, lat_max.</summary>
    public double[] Bbox { get; set; } = Array.Empty<double>();
    public List<double[][]> Land { get; set; } = new();
    public List<double[][]> Lakes { get; set; } = new();
    public List<double[][]> Borders { get; set; } = new();
    public List<RiverDefinition> Rivers { get; set; } = new();
    public List<ProvinceDefinition> Provinces { get; set; } = new();
    public bool DrawProvinces { get; set; }
    /// <summary>The neighbouring countries around a generated region.</summary>
    public List<NeighbourDefinition> Neighbours { get; set; } = new();
}

public sealed class NeighbourDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Ideology { get; set; } = "";
    public string Color { get; set; } = "#808080";
    public double Power { get; set; } = 1;
    /// <summary>Direction from the region's centre, in radians (east = 0, north = π/2).</summary>
    public double Angle { get; set; }
    public List<double[][]> Rings { get; set; } = new();
}

public sealed class RiverDefinition
{
    public string Name { get; set; } = "";
    public double[][] Points { get; set; } = Array.Empty<double[]>();
}

public sealed class ProvinceDefinition
{
    public string Name { get; set; } = "";
    public double[][] Ring { get; set; } = Array.Empty<double[]>();
    public bool Contested { get; set; }
}

/// <summary>
/// Invents plausible geography around a generated region: a noisy coastline on the side away
/// from the enemies (or none, for a landlocked region), rivers (one through the largest city),
/// a few lakes clear of the towns, and provinces (Voronoi regions, with a ring of outer regions
/// for the neighbouring lands) so the federation's edge follows borders inland and a front line
/// towards the enemy. Works in km; converts to lon/lat at the end.
/// </summary>
internal static class GeographyGenerator
{
    private const double KmPerLon = 77.3, KmPerLat = 110.6, Lon0 = 10, Lat0 = 45;

    public static GeographyDefinition Generate(
        ulong seed, IReadOnlyList<(double X, double Y)> towns, IReadOnlyList<CommuneDefinition> defs,
        double spacing, IReadOnlyList<double> enemyAngles, int largestCity)
    {
        // Its own random stream: adding geography doesn't change any map's communes or balance.
        var rng = new Rng(seed ^ 0x6E0C0A57D1A9B3E5UL);
        double minX = towns.Min(p => p.X), maxX = towns.Max(p => p.X), minY = towns.Min(p => p.Y), maxY = towns.Max(p => p.Y);
        double margin = 1.6 * spacing;
        var box = (X0: minX - margin, Y0: minY - margin, X1: maxX + margin, Y1: maxY + margin);
        var centre = ((minX + maxX) / 2, (minY + maxY) / 2);

        var geo = new GeographyDefinition
        {
            Bbox = new[] { ToLon(box.X0), ToLat(box.Y0), ToLon(box.X1), ToLat(box.Y1) },
            DrawProvinces = true,
        };

        // ── Coast ───────────────────────────────────────────────────────────
        var boxRing = new List<(double X, double Y)> { (box.X0, box.Y0), (box.X1, box.Y0), (box.X1, box.Y1), (box.X0, box.Y1) };
        List<(double X, double Y)> land = boxRing;
        List<(double X, double Y)>? coast = null;
        if (rng.Chance(0.8))
        {
            // The sea lies away from the enemies.
            double sx = -enemyAngles.Sum(Math.Cos), sy = -enemyAngles.Sum(Math.Sin);
            double seaAngle = Math.Atan2(sy, sx) + rng.Jitter(0.6);
            var d = (X: Math.Cos(seaAngle), Y: Math.Sin(seaAngle));
            var n = (X: -d.Y, Y: d.X);
            double U((double X, double Y) p) => p.X * d.X + p.Y * d.Y;
            double V((double X, double Y) p) => p.X * n.X + p.Y * n.Y;
            double reach = Math.Max(box.X1 - box.X0, box.Y1 - box.Y0);
            double vMin = V(centre) - reach, vMax = V(centre) + reach;
            // A coast just beyond the seaward towns, wandering, but never cutting a town off.
            var line = Fractal(rng, 0, 0, 7, 0.35 * spacing, vMax - vMin);
            coast = new List<(double, double)>();
            foreach (var (t, offset) in line)
            {
                double v = vMin + t * (vMax - vMin);
                double baseU = towns.Where(p => Math.Abs(V(p) - v) < 1.2 * spacing).Select(U).DefaultIfEmpty(towns.Min(U)).Max();
                double u = baseU + 0.55 * spacing + offset;
                foreach (var p in towns)
                    if (Math.Abs(V(p) - v) < 0.6 * spacing) u = Math.Max(u, U(p) + 0.3 * spacing);
                coast.Add((d.X * u + n.X * v, d.Y * u + n.Y * v));
            }
            // Land: the coast plus the far landward corners, then trimmed to the box.
            double back = towns.Min(U) - 3 * reach;
            var poly = new List<(double X, double Y)>(coast)
            {
                (d.X * back + n.X * vMax, d.Y * back + n.Y * vMax),
                (d.X * back + n.X * vMin, d.Y * back + n.Y * vMin),
            };
            land = ClipToBox(poly, box);
        }
        geo.Land.Add(Ring(land));

        // ── Lakes ───────────────────────────────────────────────────────────
        int lakes = rng.Next(3);
        for (int attempt = 0; attempt < 40 && geo.Lakes.Count < lakes; attempt++)
        {
            var c = (X: box.X0 + rng.NextDouble() * (box.X1 - box.X0), Y: box.Y0 + rng.NextDouble() * (box.Y1 - box.Y0));
            double r = spacing * (0.18 + 0.15 * rng.NextDouble());
            if (towns.Any(p => Dist(p, c) < r + 0.45 * spacing) || !Inside(land, c) || DistToPolygon(land, c) < r * 1.5) continue;
            var ring = Enumerable.Range(0, 20).Select(k =>
            {
                double a = k * 2 * Math.PI / 20, rr = r * (0.75 + 0.45 * rng.NextDouble());
                return (c.X + Math.Cos(a) * rr * 1.3, c.Y + Math.Sin(a) * rr);
            }).ToList();
            geo.Lakes.Add(Ring(ring));
        }

        // ── Rivers ──────────────────────────────────────────────────────────
        var mouthTargets = coast ?? new List<(double X, double Y)> { (box.X0, centre.Item2), (box.X1, centre.Item2), (centre.Item1, box.Y0), (centre.Item1, box.Y1) };
        var city = towns[largestCity];
        var rivers = new List<List<(double X, double Y)>> { River(rng, Source(rng, box, city, mouthTargets), city, Nearest(mouthTargets, city), spacing) };
        if (rng.Chance(0.75))
        {
            var start = Source(rng, box, centre, mouthTargets);
            rivers.Add(River(rng, start, null, Nearest(mouthTargets, start), spacing));
        }
        foreach (var r in rivers)
        {
            var inside = r.Where(p => p.X > box.X0 && p.X < box.X1 && p.Y > box.Y0 && p.Y < box.Y1).ToList();
            if (inside.Count >= 2) geo.Rivers.Add(new RiverDefinition { Name = "", Points = Ring(inside) });
        }

        // ── Provinces ───────────────────────────────────────────────────────
        // Seeds among the towns (farthest-point sampling), plus a ring outside for the
        // neighbouring lands, so the federation's provinces end at borders inside the map.
        int k = Math.Clamp(towns.Count / 5, 3, 8);
        var seeds = new List<(double X, double Y)> { towns[rng.Next(towns.Count)] };
        while (seeds.Count < k)
            seeds.Add(towns.OrderByDescending(p => seeds.Min(s => Dist(p, s)) + rng.NextDouble() * spacing * 0.3).First());
        int inner = seeds.Count;
        double ringR = Math.Max(maxX - minX, maxY - minY) / 2 + 1.3 * spacing;
        for (int i = 0; i < 12; i++)
        {
            double a = i * Math.PI / 6 + rng.Jitter(0.15);
            seeds.Add((centre.Item1 + Math.Cos(a) * ringR, centre.Item2 + Math.Sin(a) * ringR));
        }
        var fronts = Enumerable.Range(0, defs.Count).Where(i => defs[i].FrontOf.Length > 0).ToHashSet();
        string[] provinceNames = { "North", "South", "East", "West", "Upper", "Lower", "Coast", "Hills" };
        var outer = seeds.Skip(inner).ToList();
        var innerSeeds = seeds.Take(inner).ToList();
        var outerAngles = Enumerable.Range(0, outer.Count).Select(i => Math.Atan2(outer[i].Y - centre.Item2, outer[i].X - centre.Item1)).ToList();
        while (true)
        {
            var all = innerSeeds.Concat(outer).ToList();
            var cells = new List<List<(double X, double Y)>>();
            for (int i = 0; i < innerSeeds.Count; i++)
            {
                var cell = new List<(double X, double Y)>(boxRing);
                for (int j = 0; j < all.Count && cell.Count > 0; j++)
                    if (j != i) cell = ClipCloser(cell, all[i], all[j]);
                cells.Add(cell);
            }
            // An edge town nearer a neighbouring land's seed than any of ours would fall outside
            // every province (and lose its territory): make it a province seed and try again.
            int stray = Enumerable.Range(0, towns.Count).FirstOrDefault(t => !cells.Any(c => c.Count >= 3 && Inside(c, towns[t])), -1);
            if (stray >= 0)
            {
                innerSeeds.Add(towns[stray]);
                continue;
            }
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].Count < 3) continue;
                var members = Enumerable.Range(0, towns.Count).Where(t => Inside(cells[i], towns[t])).ToList();
                if (members.Count == 0) continue;
                geo.Provinces.Add(new ProvinceDefinition
                {
                    Name = provinceNames[i % provinceNames.Length],
                    Ring = Ring(cells[i]),
                    // Provinces on the front are held only near the towns; the rest are held entire.
                    Contested = members.Any(fronts.Contains),
                });
            }
            break;
        }

        // Neighbouring countries: the outer cells, grouped in runs of neighbouring seeds and
        // clipped to the land, so the region sits among real-looking states.
        var allSeeds = innerSeeds.Concat(outer).ToList();
        int groups = 3 + rng.Next(3);
        int startSeed = rng.Next(outer.Count);
        for (int g = 0; g < groups; g++)
        {
            var neighbour = new NeighbourDefinition { Id = $"N{g + 1}" };
            var members = Enumerable.Range(0, outer.Count).Where(i => ((i - startSeed + outer.Count) % outer.Count) * groups / outer.Count == g).ToList();
            foreach (int i in members)
            {
                int self = innerSeeds.Count + i;
                var cell = new List<(double X, double Y)>(boxRing);
                for (int j = 0; j < allSeeds.Count && cell.Count > 0; j++)
                    if (j != self) cell = ClipCloser(cell, allSeeds[self], allSeeds[j]);
                if (cell.Count < 3) continue;
                var piece = ClipToConvex(land, cell);
                if (piece.Count >= 3) neighbour.Rings.Add(Ring(piece));
            }
            if (neighbour.Rings.Count == 0) continue;
            double sx = members.Sum(i => Math.Cos(outerAngles[i])), sy = members.Sum(i => Math.Sin(outerAngles[i]));
            neighbour.Angle = Math.Atan2(sy, sx);
            geo.Neighbours.Add(neighbour);
        }
        return geo;
    }

    /// <summary>Midpoint displacement: (t in 0..1, offset) pairs for a wandering line.</summary>
    private static List<(double T, double Offset)> Fractal(Rng rng, double a, double b, int depth, double amplitude, double length)
    {
        var pts = new List<(double T, double Offset)> { (0, a), (1, b) };
        double amp = amplitude * 2.2;
        for (int level = 0; level < depth; level++, amp *= 0.55)
        {
            var next = new List<(double, double)>();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                next.Add(pts[i]);
                next.Add(((pts[i].T + pts[i + 1].T) / 2, (pts[i].Offset + pts[i + 1].Offset) / 2 + rng.Jitter(amp)));
            }
            next.Add(pts[^1]);
            pts = next;
        }
        return pts;
    }

    private static (double X, double Y) Source(Rng rng, (double X0, double Y0, double X1, double Y1) box, (double X, double Y) near, List<(double X, double Y)> mouths)
    {
        // Rivers rise inland, on the side away from the sea.
        var mouth = Nearest(mouths, near);
        var away = (X: near.X - mouth.X, Y: near.Y - mouth.Y);
        double len = Math.Max(1e-6, Math.Sqrt(away.X * away.X + away.Y * away.Y));
        double reach = Math.Max(box.X1 - box.X0, box.Y1 - box.Y0);
        var s = (X: near.X + away.X / len * reach * 0.8 + rng.Jitter(reach * 0.15), Y: near.Y + away.Y / len * reach * 0.8 + rng.Jitter(reach * 0.15));
        return (Math.Clamp(s.X, box.X0 - 1, box.X1 + 1), Math.Clamp(s.Y, box.Y0 - 1, box.Y1 + 1));
    }

    /// <summary>A meandering course from source to mouth, through <paramref name="via"/> if given.</summary>
    private static List<(double X, double Y)> River(Rng rng, (double X, double Y) source, (double X, double Y)? via, (double X, double Y) mouth, double spacing)
    {
        var legs = via is { } v ? new[] { (source, v), (v, mouth) } : new[] { (source, mouth) };
        var course = new List<(double X, double Y)>();
        foreach (var (a, b) in legs)
        {
            double len = Dist(a, b);
            var nx = -(b.Y - a.Y) / Math.Max(len, 1e-6);
            var ny = (b.X - a.X) / Math.Max(len, 1e-6);
            foreach (var (t, off) in Fractal(rng, 0, 0, 6, 0.12 * len, len))
            {
                if (course.Count > 0 && t == 0) continue;
                course.Add((a.X + (b.X - a.X) * t + nx * off, a.Y + (b.Y - a.Y) * t + ny * off));
            }
        }
        return course;
    }

    private static (double X, double Y) Nearest(List<(double X, double Y)> pts, (double X, double Y) to) => pts.OrderBy(p => Dist(p, to)).First();

    /// <summary>Keeps the part of the polygon closer to a than to b.</summary>
    private static List<(double X, double Y)> ClipCloser(List<(double X, double Y)> poly, (double X, double Y) a, (double X, double Y) b)
    {
        var mid = ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        var normal = (b.X - a.X, b.Y - a.Y);
        return ClipHalfPlane(poly, p => (p.X - mid.Item1) * normal.Item1 + (p.Y - mid.Item2) * normal.Item2);
    }

    /// <summary>Clips any polygon by a convex one (Sutherland–Hodgman, one edge of the convex polygon at a time).</summary>
    private static List<(double X, double Y)> ClipToConvex(List<(double X, double Y)> subject, List<(double X, double Y)> convex)
    {
        var cx = convex.Average(p => p.X);
        var cy = convex.Average(p => p.Y);
        var result = subject;
        for (int i = 0; i < convex.Count && result.Count > 0; i++)
        {
            var a = convex[i];
            var b = convex[(i + 1) % convex.Count];
            double Cross((double X, double Y) p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            double inside = Math.Sign(Cross((cx, cy)));
            result = ClipHalfPlane(result, p => -inside * Cross(p));
        }
        return result;
    }

    private static List<(double X, double Y)> ClipToBox(List<(double X, double Y)> poly, (double X0, double Y0, double X1, double Y1) box)
    {
        poly = ClipHalfPlane(poly, p => box.X0 - p.X);
        poly = ClipHalfPlane(poly, p => p.X - box.X1);
        poly = ClipHalfPlane(poly, p => box.Y0 - p.Y);
        poly = ClipHalfPlane(poly, p => p.Y - box.Y1);
        // Snap to the box exactly so the map knows which edges are the box, not coast.
        return poly.Select(p => (Snap(p.X, box.X0, box.X1), Snap(p.Y, box.Y0, box.Y1))).ToList();
    }

    private static double Snap(double v, double lo, double hi) => Math.Abs(v - lo) < 1e-6 ? lo : Math.Abs(v - hi) < 1e-6 ? hi : v;

    /// <summary>Sutherland–Hodgman against side(p) ≤ 0.</summary>
    private static List<(double X, double Y)> ClipHalfPlane(List<(double X, double Y)> poly, Func<(double X, double Y), double> side)
    {
        var result = new List<(double X, double Y)>(poly.Count + 2);
        for (int i = 0; i < poly.Count; i++)
        {
            var cur = poly[i];
            var next = poly[(i + 1) % poly.Count];
            double sc = side(cur), sn = side(next);
            if (sc <= 0) result.Add(cur);
            if ((sc < 0 && sn > 0) || (sc > 0 && sn < 0))
            {
                double t = sc / (sc - sn);
                result.Add((cur.X + (next.X - cur.X) * t, cur.Y + (next.Y - cur.Y) * t));
            }
        }
        return result;
    }

    private static bool Inside(List<(double X, double Y)> poly, (double X, double Y) p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }

    private static double DistToPolygon(List<(double X, double Y)> poly, (double X, double Y) p)
    {
        double best = double.MaxValue;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            double t = l2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2, 0, 1);
            best = Math.Min(best, Dist(p, (a.X + t * dx, a.Y + t * dy)));
        }
        return best;
    }

    private static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double ToLon(double x) => Math.Round(Lon0 + x / KmPerLon, 4);
    private static double ToLat(double y) => Math.Round(Lat0 + y / KmPerLat, 4);

    private static double[][] Ring(IEnumerable<(double X, double Y)> pts) => pts.Select(p => new[] { ToLon(p.X), ToLat(p.Y) }).ToArray();
}
