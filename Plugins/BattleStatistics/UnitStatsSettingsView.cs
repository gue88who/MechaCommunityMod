using UnityEngine;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal sealed class UnitStatsSettingsView
{
    internal bool Open { get; private set; }
    private float _scroll, _dragY;
    private int _dragControl;
    private GUIStyle? _label, _button;
    internal void Toggle() { if (Open) Close(); else { Open = true; _scroll = 0; } }
    internal void Close()
    {
        if (_dragControl != 0 && GUIUtility.hotControl == _dragControl) GUIUtility.hotControl = 0;
        _dragControl = 0; Open = false;
    }

    internal void Draw(Rect rect, ResultsSkin skin, TableDisplaySettings settings)
    {
        _label ??= new GUIStyle { font = skin.Body, fontSize = 16, alignment = TextAnchor.MiddleLeft };
        _button ??= new GUIStyle { font = skin.Body, fontSize = 14, alignment = TextAnchor.MiddleCenter };
        foreach (var style in new[] { _label, _button })
            style.normal.textColor = style.hover.textColor = style.active.textColor = Color.white;
        var groups = new[] {
            ("ATTACK", new[] { ("Kills / spell uses", settings.Kills), ("Damage", settings.Dealt),
                ("Overkill", settings.TableDamageOverkill), ("Core dmg", settings.CoreDamage),
                ("Hacked", settings.Hacking), ("Hack amount", settings.TableHackAmount), ("Failed hack", settings.TableFailedHack) }),
            ("DEFENSE", new[] { ("Tanked", settings.Tanked), ("Overkill", settings.TableTankedOverkill) }),
            ("XP", new[] { ("Gained", settings.TableXpGained), ("Fed", settings.TableXpFed) }),
            ("UNITS", new[] { ("Supply", settings.Costs), ("Levels", settings.Levels),
                ("Source breakdowns", settings.Sources), ("Attributed units", settings.Contributions) }) };
        var maximum = Math.Max(0, groups.Sum(g => 30 + g.Item2.Length * 32) - rect.height);
        _scroll = Math.Clamp(_scroll, 0, maximum);
        var input = Event.current;
        if (input.type == EventType.ScrollWheel && rect.Contains(input.mousePosition))
        { _scroll = Math.Clamp(_scroll + input.delta.y * 28, 0, maximum); input.Use(); }
        var drag = GUIUtility.GetControlID(718, FocusType.Passive);
        GUI.BeginGroup(rect);
        try
        {
            var y = -_scroll;
            // The list remains readable in a wide unit popup, with controls nearby.
            var width = Math.Min(380, rect.width);
            foreach (var (title, options) in groups)
            {
                skin.Art(new Rect(0, y, width, 26), "panel_blue_30_with_corners");
                GUI.Label(new Rect(10, y, width - 20, 26), title, _label);
                y += 30;
                foreach (var (caption, entry) in options)
                {
                    GUI.Label(new Rect(10, y, width - 100, 28), caption, _label);
                    var toggle = new Rect(width - 82, y, 72, 28);
                    skin.Art(toggle, entry.Value ? "button_y_x1" : "button_b_x1");
                    if (GUI.Button(toggle, entry.Value ? "ON" : "OFF", _button)) entry.Value = !entry.Value;
                    y += 32;
                }
            }
        }
        finally { GUI.EndGroup(); }
        if (input.type == EventType.MouseDown && input.button == 0 && rect.Contains(input.mousePosition) && GUIUtility.hotControl == 0)
        { GUIUtility.hotControl = _dragControl = drag; _dragY = input.mousePosition.y; input.Use(); }
        else if (_dragControl == drag && GUIUtility.hotControl == drag)
        {
            if (input.rawType == EventType.MouseDrag)
            { _scroll = Math.Clamp(_scroll + _dragY - input.mousePosition.y, 0, maximum); _dragY = input.mousePosition.y; input.Use(); }
            else if (input.rawType == EventType.MouseUp) { GUIUtility.hotControl = 0; _dragControl = 0; input.Use(); }
        }
        if (rect.Contains(input.mousePosition) && input.type is EventType.MouseDown or EventType.MouseUp or EventType.ScrollWheel) input.Use();
    }
}
