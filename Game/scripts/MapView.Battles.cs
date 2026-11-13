using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// Battlefields on the map: where a front commune was fought over last turn, a marker between
/// its militia and the army attacking it, ringed in the colour of how the battle went (green
/// repulsed, amber held, red ground lost or fallen) and pulsing; clicking it opens the battle.
/// </summary>
public partial class MapView
{
    /// <summary>A battlefield was clicked: the commune fought over.</summary>
    public event Action<int>? BattleClicked;

    public static Color EnemyColorOf(World w, int enemy) => EnemyColor(w, enemy);

    public static Color ResultColor(BattleResult result) => result switch
    {
        BattleResult.Repulsed => new Color("70c070"),
        BattleResult.Held => new Color("e0b050"),
        _ => new Color("e05040"),
    };

    /// <summary>Last turn's battles and where each was fought, in the map's local coordinates.</summary>
    private IEnumerable<(BattleReport Report, Vector2 At)> BattleSpots()
    {
        if (World is not { } w || AtWorldZoom) yield break;
        foreach (var r in w.Battles.Where(b => b.Tick == w.Tick - 1))
        {
            var militia = MilitiaSpots.FirstOrDefault(m => m.Commune == r.Commune);
            var army = ArmySpots.Where(a => a.Commune == r.Commune).Select(a => (Vector2?)a.At).FirstOrDefault();
            if (militia.At == default && army is null) continue;
            var at = militia.At == default ? army!.Value
                : army is { } a ? militia.At.Lerp(a, 0.5f)
                : militia.At + (militia.Facing - militia.At) * 0.6f;
            yield return (r, at);
        }
    }

    /// <summary>The commune whose battlefield is under the point, if any.</summary>
    private int? HitBattle(Vector2 at)
    {
        float best = 26 * 26;
        int? hit = null;
        foreach (var (r, spot) in BattleSpots())
        {
            float d = spot.DistanceSquaredTo(at);
            if (d < best) { best = d; hit = r.Commune; }
        }
        return hit;
    }

    /// <summary>The markers, on the 3D map's overlay (`place` takes a point of the flat map to the screen).</summary>
    public void DrawBattleMarkers(CanvasItem target, Func<Vector2, Vector2?> place)
    {
        float t = Time.GetTicksMsec() / 1000f;
        foreach (var (r, spot) in BattleSpots())
        {
            if (place(spot) is not { } p) continue;
            var color = ResultColor(r.Result);
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 3 + r.Commune);
            target.DrawCircle(p, 15, new Color(0.08f, 0.07f, 0.06f, 0.78f));
            target.DrawArc(p, 15, 0, Mathf.Tau, 32, color, 2.5f, true);
            target.DrawArc(p, 17 + 5 * pulse, 0, Mathf.Tau, 32, color with { A = 0.55f * (1 - pulse) }, 2, true);
            // Crossed swords: two blades, their guards and pommels.
            foreach (float s in new[] { -1f, 1f })
            {
                var dir = new Vector2(s, -1).Normalized();
                var tip = p + dir * 9;
                var hilt = p - dir * 7;
                target.DrawLine(hilt, tip, new Color(0.92f, 0.9f, 0.85f), 2.2f, true);
                var guard = dir.Orthogonal() * 3.5f;
                target.DrawLine(hilt + dir * 2 - guard, hilt + dir * 2 + guard, color, 2, true);
                target.DrawCircle(hilt - dir * 1.5f, 1.8f, color);
            }
        }
    }
}
