using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// The ledger: the federation's history at a glance, as a grid of
/// small charts (stores, scarcity prices, wellbeing, enthusiasm, militia, gold, science,
/// culture, drift, communes, trust, influence, the enemy), each with its current value and range.
/// </summary>
public partial class LedgerView : GridContainer
{
    private readonly World _world;

    public LedgerView(World world)
    {
        _world = world;
        Columns = 3;
        AddThemeConstantOverride("h_separation", 14);
        AddThemeConstantOverride("v_separation", 14);
    }

    private static readonly (string Title, string Icon, Func<WeekRecord, double> Value, string Format, Color Color)[] Series =
    {
        ("Food in store", "food", r => r.Food, "0", new Color("e9c46a")),
        ("Goods in store", "goods", r => r.Goods, "0", new Color("b8c1cc")),
        ("Price of food", "food", r => r.FoodPrice, "0.00", new Color("f4a261")),
        ("Price of goods", "goods", r => r.GoodsPrice, "0.00", new Color("a8b8c8")),
        ("Wellbeing", "wellbeing", r => r.Wellbeing * 100, "0'%'", new Color("ffd166")),
        ("Enthusiasm", "enthusiasm", r => r.Enthusiasm * 100, "0'%'", new Color("f4845f")),
        ("Militia (thousands)", "militia_icon", r => r.Fighters, "0.0", new Color("e05d5d")),
        ("Gold", "gold", r => r.Gold, "0", new Color("f4c542")),
        ("Science a turn", "science", r => r.Science, "0.0", new Color("7fc8f8")),
        ("Culture", "culture", r => r.Culture * 100, "0'%'", new Color("c77dff")),
        ("Hierarchy drift", "drift", r => r.Drift, "0.0", new Color("e07a5f")),
        ("Communes in the federation", "commune", r => r.Federated, "0", new Color("8fc98f")),
        ("Trust between neighbours", "trust", r => r.MeanTrust * 100, "0'%'", new Color("b39ddb")),
        ("Influence", "influence", r => r.Influence, "0", new Color("e6d3a3")),
        ("Enemy strength", "enemy", r => r.EnemyStrength, "0.0", new Color("e63946")),
    };

    public override void _Ready()
    {
        foreach (var s in Series) AddChild(new SparkCard(_world, s.Title, s.Icon, s.Value, s.Format, s.Color));
    }

    private partial class SparkCard : PanelContainer
    {
        private readonly World _w;
        private readonly string _title, _icon, _format;
        private readonly Func<WeekRecord, double> _value;
        private readonly Color _color;

        public SparkCard(World w, string title, string icon, Func<WeekRecord, double> value, string format, Color color)
        {
            (_w, _title, _icon, _value, _format, _color) = (w, title, icon, value, format, color);
            CustomMinimumSize = new Vector2(330, 150);
            AddThemeStyleboxOverride("panel", UiTheme.FramePanel(10));
        }

        public override void _Ready()
        {
            var v = new VBoxContainer();
            AddChild(v);
            var head = new HBoxContainer();
            head.AddChild(UiTheme.IconRect(_icon, 18));
            var t = new Label { Text = _title, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            t.AddThemeFontOverride("font", UiTheme.Display);
            t.AddThemeColorOverride("font_color", UiTheme.GoldBright);
            head.AddChild(t);
            var h = _w.History;
            var now = new Label { Text = h.Count == 0 ? "—" : _value(h[^1]).ToString(_format) };
            now.AddThemeFontOverride("font", UiTheme.BodyBold);
            now.AddThemeFontSizeOverride("font_size", 18);
            head.AddChild(now);
            v.AddChild(head);
            v.AddChild(new Spark(_w, _value, _format, _color) { CustomMinimumSize = new Vector2(0, 92), SizeFlagsVertical = SizeFlags.ExpandFill });
        }
    }

    private partial class Spark : Control
    {
        private readonly World _w;
        private readonly Func<WeekRecord, double> _value;
        private readonly string _format;
        private readonly Color _color;

        public Spark(World w, Func<WeekRecord, double> value, string format, Color color) => (_w, _value, _format, _color) = (w, value, format, color);

        public override void _Draw()
        {
            var h = _w.History;
            var font = UiTheme.Body;
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.25f));
            if (h.Count < 2)
            {
                DrawString(font, new Vector2(8, Size.Y / 2), "Not enough history yet.", HorizontalAlignment.Left, -1, 13, UiTheme.Muted);
                return;
            }
            var values = h.Select(_value).ToArray();
            double min = values.Min(), max = values.Max();
            double lo = min, hi = max;
            if (hi - lo < 1e-6) { hi += 1; lo -= 1; } // a flat line, drawn in the middle
            float left = 4, right = Size.X - 4, top = 16, bottom = Size.Y - 14;
            Vector2 P(int i) => new(left + (right - left) * i / (values.Length - 1), bottom - (float)((values[i] - lo) / (hi - lo)) * (bottom - top));
            // Shaded area under the line, then the line.
            var area = new List<Vector2> { new(left, bottom) };
            for (int i = 0; i < values.Length; i++) area.Add(P(i));
            area.Add(new Vector2(right, bottom));
            if (Geometry2D.TriangulatePolygon(area.ToArray()).Length > 0) DrawColoredPolygon(area.ToArray(), new Color(_color, 0.16f));
            DrawPolyline(Enumerable.Range(0, values.Length).Select(P).ToArray(), _color, 2, true);
            DrawString(font, new Vector2(left + 2, top - 3), $"max {max.ToString(_format)}", HorizontalAlignment.Left, -1, 11, UiTheme.Muted);
            DrawString(font, new Vector2(left + 2, Size.Y - 2), $"min {min.ToString(_format)}", HorizontalAlignment.Left, -1, 11, UiTheme.Muted);
            string end = _w.Format(_w.DateAt(h[^1].Tick), "MMM yyyy");
            DrawString(font, new Vector2(right - 90, Size.Y - 2), end, HorizontalAlignment.Right, 88, 11, UiTheme.Muted);
        }
    }
}
