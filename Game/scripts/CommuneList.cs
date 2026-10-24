using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// Sortable table of every commune, docked over the map. Click a column title to sort,
/// click a row to select the commune and centre the map on it.
/// </summary>
public partial class CommuneList : PanelContainer
{
    private static readonly string[] Titles = { "Commune", "Pop", "Trust", "Well-being", "Pract.", "Polit.", "Discont.", "Status", "Food", "Goods", "Militia" };

    private Tree _tree = null!;
    private int _sortColumn = 6; // discontent: the communes most likely to leave first
    private bool _descending = true;
    private bool _refreshing;

    public World? World { get; set; }

    /// <summary>Raised with the commune id when a row is clicked.</summary>
    public event Action<int>? CommuneChosen;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(1000, 400);
        AddThemeStyleboxOverride("panel", UiTheme.FrameStrong(22));
        var v = new VBoxContainer();
        AddChild(v);
        v.AddChild(UiTheme.SectionTitle("Communes", "commune"));
        var hint = new Label { Text = "Click a title to sort, a row to select. C closes.", Modulate = new Color(1, 1, 1, 0.55f) };
        hint.AddThemeFontSizeOverride("font_size", 13);
        v.AddChild(hint);

        _tree = new Tree
        {
            Columns = Titles.Length,
            ColumnTitlesVisible = true,
            HideRoot = true,
            SelectMode = Tree.SelectModeEnum.Row,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        for (int i = 0; i < Titles.Length; i++)
        {
            _tree.SetColumnTitle(i, Titles[i]);
            _tree.SetColumnExpand(i, i is 0 or 7);
            _tree.SetColumnCustomMinimumWidth(i, i switch { 0 => 130, 3 => 80, 7 => 150, 8 or 9 or 10 => 70, _ => 58 });
        }
        _tree.ColumnTitleClicked += (column, _) =>
        {
            if ((int)column == _sortColumn) _descending = !_descending;
            else
            {
                _sortColumn = (int)column;
                _descending = _sortColumn != 0;
            }
            Refresh();
        };
        _tree.ItemSelected += () =>
        {
            if (!_refreshing && _tree.GetSelected() is { } item) CommuneChosen?.Invoke(item.GetMetadata(0).AsInt32());
        };
        v.AddChild(_tree);
    }

    /// <summary>
    /// Scrolling over the list scrolls the list, never the map beneath it. A trackpad or Magic
    /// Mouse swipe arrives as a pan gesture, which the table doesn't take itself.
    /// </summary>
    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventPanGesture pan:
                var bar = _tree.GetChildren(true).OfType<VScrollBar>().FirstOrDefault();
                if (bar != null) bar.Value += pan.Delta.Y * 24;
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight }:
            case InputEventMagnifyGesture:
                AcceptEvent();
                break;
        }
    }

    public void Refresh()
    {
        if (World is not { } w || !IsVisibleInTree()) return;
        _refreshing = true;
        _tree.Clear();
        var root = _tree.CreateItem();
        var occupiedLast = w.Communes.OrderBy(c => c.Occupied);
        var rows = _descending ? occupiedLast.ThenByDescending(c => Key(w, c)) : occupiedLast.ThenBy(c => Key(w, c));
        foreach (var c in rows)
        {
            var item = _tree.CreateItem(root);
            item.SetMetadata(0, c.Id);
            double trust = w.Trust.AverageTrust(c.Id);
            item.SetText(0, c.Name);
            item.SetText(1, $"{c.Population:0}");
            item.SetText(2, $"{trust:P0}");
            item.SetText(3, $"{c.Wellbeing:P0}");
            item.SetText(4, $"{c.PracticalEducation:0.00}");
            item.SetText(5, $"{c.PoliticalEducation:0.00}");
            item.SetText(6, $"{c.Discontent:0.00}");
            item.SetText(7, Status(w, c));
            // Stores, in turns of the commune's own needs: how long it can last on its own.
            item.SetText(8, $"{FoodTurns(w, c):0} {w.Turn[0]}");
            item.SetText(9, $"{GoodsTurns(w, c):0} {w.Turn[0]}");
            item.SetTooltipText(8, $"Food {Math.Max(0, c.Food):0} in store, +{c.FoodProduced:0.0} a {w.Turn}, eats {w.FoodDemand(c):0.0}");
            item.SetText(10, $"{c.Mobilization:P0}");
            item.SetTooltipText(10, $"{c.Population * c.Mobilization:0.0} thousand under arms");
            item.SetTooltipText(9, $"Goods {Math.Max(0, c.Goods):0} in store, +{c.GoodsProduced:0.0} a {w.Turn}, uses {w.GoodsDemand(c):0.0}");
            if (c.Occupied)
                for (int i = 0; i < Titles.Length; i++) item.SetCustomColor(i, new Color(1, 1, 1, 0.35f));
            else
            {
                item.SetCustomColor(2, Grade(trust));
                item.SetCustomColor(3, Grade(c.Wellbeing));
                if (c.ExpertGap > 0.1) item.SetCustomColor(4, new Color(0.95f, 0.6f, 0.2f));
                if (c.Discontent > w.Balance.SecessionThreshold) item.SetCustomColor(6, new Color(0.95f, 0.45f, 0.4f));
                if (c.Starving || c.Front || w.PendingSecessions.Any(s => s.Commune == c.Id)) item.SetCustomColor(7, new Color(0.95f, 0.55f, 0.4f));
                item.SetCustomColor(8, Grade(Math.Min(1, FoodTurns(w, c) / 10)));
                item.SetCustomColor(9, Grade(Math.Min(1, GoodsTurns(w, c) / 10)));
            }
        }
        for (int i = 0; i < Titles.Length; i++)
            _tree.SetColumnTitle(i, Titles[i] + (i == _sortColumn ? (_descending ? " ▼" : " ▲") : ""));
        _refreshing = false;
    }

    private IComparable Key(World w, Commune c) => _sortColumn switch
    {
        0 => c.Name,
        1 => c.Population,
        2 => w.Trust.AverageTrust(c.Id),
        3 => c.Wellbeing,
        4 => c.PracticalEducation,
        5 => c.PoliticalEducation,
        6 => c.Discontent,
        8 => FoodTurns(w, c),
        9 => GoodsTurns(w, c),
        10 => c.Mobilization,
        _ => Status(w, c),
    };

    private static double FoodTurns(World w, Commune c) => Math.Max(0, c.Food) / Math.Max(0.01, w.FoodDemand(c));
    private static double GoodsTurns(World w, Commune c) => Math.Max(0, c.Goods) / Math.Max(0.01, w.GoodsDemand(c));

    private static string Status(World w, Commune c)
    {
        if (c.OccupiedBy is int by) return $"occupied ({w.Enemies[by].Name})";
        if (!c.Federated) return "seceded";
        var tags = new List<string>();
        if (w.PendingSecessions.Any(s => s.Commune == c.Id)) tags.Add("leaving");
        if (c.Front) tags.Add($"front {c.Control:P0}");
        if (c.Starving) tags.Add("starving");
        if (c.FreeRiding > 0.2) tags.Add("free-riding");
        return tags.Count == 0 ? "" : string.Join(", ", tags);
    }

    private static Color Grade(double v) =>
        v < 0.4 ? new Color(0.95f, 0.45f, 0.4f) : v < 0.6 ? new Color(0.95f, 0.8f, 0.35f) : new Color(0.55f, 0.85f, 0.6f);
}

/// <summary>Short-lived notices stacked at the top of the map.</summary>
public partial class ToastStack : VBoxContainer
{
    private const int MaxToasts = 3;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 6);
    }

    public void Show(string text, Color accent, double seconds = 6)
    {
        while (GetChildCount() >= MaxToasts) GetChild(0).Free();
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.07f, 0.08f, 0.94f),
            BorderColor = accent, BorderWidthLeft = 4, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ShadowColor = new Color(0, 0, 0, 0.45f), ShadowSize = 6,
            CornerRadiusTopRight = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 0) };
        label.AddThemeFontSizeOverride("font_size", 13);
        panel.AddChild(label);
        AddChild(panel);
        GetTree().CreateTimer(seconds).Timeout += () =>
        {
            if (IsInstanceValid(panel)) panel.QueueFree();
        };
    }
}
