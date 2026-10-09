using UnityEngine;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal sealed class StatsHelpView
{
    private readonly bool _live;
    internal StatsHelpView(bool live = false) => _live = live;
    internal bool Open { get; private set; }
    private float _scroll;
    private int _dragControl;
    private float _dragY;
    private GUIStyle? _title, _body, _button;
    private static readonly (string Title, string Description)[] Topics = {
        ("Damage", "Health removed from enemies, excluding overkill. Hiding overkill does not add it to Damage."),
        ("Core dmg", "Damage to the opposing player's HP after a round. Spawned and hacked units count toward their parent."),
        ("Dmg overkill", "A 200-damage hit against 50 health records 50 Damage and 150 overkill. Spell overkill is excluded."),
        ("Tanked", "Health damage received, excluding incoming overkill."),
        ("Tank overkill", "Incoming excess damage. A 200-damage hit against 50 health records 50 Tanked and 150 Tank overkill."),
        ("Kills", "Enemy units destroyed."),
        ("Hacked", "Combat units successfully converted to your side. Failed or unfinished hacking attempts are not counted. Captured units appear under the Hacker and their subsequent contributions are included in its totals."),
        ("Hack amount", "Hack progress spent on successful conversions. Each Hacker keeps its contribution."),
        ("Failed hack", "Hack progress spent on attempts that ended without conversion."),
        ("XP gained", "Experience actually added to this squad after the game's modifiers and caps. Fractional XP is retained. XP rejected by a cap is not counted as gained."),
        ("XP fed", "Experience actually awarded to enemies from damaging or destroying this unit. It uses the enemy's observed XP gain; rejected or wasted XP is not counted."),
        ("Supply", "Deployment value: squad cost and level upgrades, plus technologies for grouped types."),
        ("Unit levels", "The circle shows level and XP toward the next level. Hover for details. Maximum-level units have no progress fill."),
        ("Parent and child rows", "The units button expands spawned or attributed units; + expands sources. Child stats already count toward the parent. Child xN counts recorded units or squads; spell xN counts casts."),
        ("Source breakdowns", "+ shows attacks, technologies and other effects."),
        ("Rounds and unavailable values", "All combines recorded rounds. Unit stats lists rounds where the squad was present. A dash means zero or unavailable. Starting mid-match can leave gaps."),
        ("Board map", "The eye toggles the recorded starting formation and highlights the squad. Sold units use their latest snapshot."),
        ("Settings and controls", "Settings save automatically and preserve recorded data. Click numeric headers to sort, drag to scroll, and player names to toggle armies. F8 toggles the live overlay.")
    };
    private static readonly (string Title, string Description, Color[] Colors)[] LiveTopics = {
        ("Reading the bars", "Each row combines one unit type. Upper bar: received; lower bar: dealt. With separate overkill, effective damage is left and excess is right. Widths compare shares of the army total across all pages.", Array.Empty<Color>()),
        ("Damage", "Health removed from enemies. Regular attacks use this red color in the lower bar. With separate overkill enabled, the left amount is effective damage and the right amount beside the purple marker is excess damage.", new[] { DamagePalette.ColorFor(DamageCategory.Attack, true) }),
        ("Damage overkill", "The purple end of the lower bar is excess damage beyond the target's remaining health. A 200-damage hit against 50 health records 50 damage + 150 overkill. Spell overkill is excluded.", new[] { DamagePalette.DamageOverkill }),
        ("Tanked", "Health damage received. Regular attacks use this blue color in the upper bar. With separate overkill enabled, the left amount is health removed and the right amount beside the yellow marker is incoming excess damage.", new[] { DamagePalette.ColorFor(DamageCategory.Attack, false) }),
        ("Tanked overkill", "The yellow end of the upper bar is incoming excess damage from lethal hits. In the 200-versus-50 example, the victim records 50 tanked + 150 tanked overkill.", new[] { DamagePalette.TankedOverkill }),
        ("Technologies", "Orange segments show damage from technologies. This source color can appear in either bar: damage dealt or damage received. Incoming technology belongs to whoever caused it.", new[] { DamagePalette.ColorFor(DamageCategory.Tech, true) }),
        ("Spells", "Teal segments show spell damage in either bar. A spell's spawned units are attributed to the spell in the detailed statistics. Spell overkill is excluded from the overlay's totals.", new[] { DamagePalette.ColorFor(DamageCategory.Spell, true) }),
        ("Other / unknown sources", "Gray segments show other effects or damage whose source was not observed (something to report to the dev of this mod). They remain part of the totals. Source coloring can be disabled in Settings.", new[] { DamagePalette.ColorFor(DamageCategory.Other, true) }),
        ("Hack amount", "Hack progress spent on successful conversions, including the final pulse. Each Hacker keeps its contribution; each converted unit counts once.", new[] { DamagePalette.HackAmount }),
        ("Failed hack", "Hack progress spent on attempts that ended without conversion. Active attempts are excluded. Both overlay hack bars share a scale across all pages.", new[] { DamagePalette.FailedHack }),
        ("Kills / spell uses", "The smaller line below the combat bars shows enemy combat units destroyed. Uses is the number of spell casts. Hacked is the number of successful conversions; it is separate from kills.", Array.Empty<Color>()),
        ("Experience", "XP gained shows experience actually added after modifiers and caps. XP fed is actual experience awarded to enemies through that unit's death calculation. XP rejected by caps is excluded.", Array.Empty<Color>()),
        ("Combined values and controls", "Hide separate overkill to combine damage and excess. Arrows change pages; the toggle key hides panels. After game over, panels show the last fight.", Array.Empty<Color>())
    };

    internal void Toggle() { if (Open) Close(); else { Open = true; _scroll = 0; } }
    internal void Close()
    {
        if (_dragControl != 0 && GUIUtility.hotControl == _dragControl) GUIUtility.hotControl = 0;
        _dragControl = 0; Open = false;
    }
    internal void Draw(Rect rect, ResultsSkin skin)
    {
        _title ??= new GUIStyle { font = skin.Title, fontSize = 16, alignment = TextAnchor.MiddleLeft };
        _body ??= new GUIStyle { font = skin.Body, fontSize = 16, wordWrap = true, alignment = TextAnchor.UpperLeft };
        _button ??= new GUIStyle { font = skin.Body, fontSize = 16, alignment = TextAnchor.MiddleCenter };
        foreach (var style in new[] { _title, _body, _button })
            style.normal.textColor = style.hover.textColor = style.active.textColor = Color.white;
        var oldColor = GUI.color;
        try
        {
            GUI.color = new Color(0.035f, 0.047f, 0.075f, 1); GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = Color.white;
            skin.Art(rect, "replayUI_frame_2");
            skin.Art(new Rect(rect.x + 12, rect.y + 12, rect.width - 66, 40), "button_b_sp1");
            GUI.Label(new Rect(rect.x + 26, rect.y + 20, rect.width - 100, 26), _live ? "LIVE OVERLAY GUIDE" : "STATS GUIDE", _title);
            var close = new Rect(rect.xMax - 44, rect.y + 16, 30, 30);
            skin.Art(close, "button_b_x1");
            if (GUI.Button(close, "X", _button)) { Close(); return; }
            var view = new Rect(rect.x + 22, rect.y + 66, rect.width - 44, rect.height - 88);
            var rowHeight = rect.width < 600 ? 156 : 116;
            var count = _live ? LiveTopics.Length : Topics.Length;
            var max = Math.Max(0, count * rowHeight - view.height);
            var drag = GUIUtility.GetControlID(107, FocusType.Passive);
            _scroll = Math.Clamp(_scroll, 0, max);
            if (Event.current.type == EventType.ScrollWheel && rect.Contains(Event.current.mousePosition))
            { _scroll = Math.Clamp(_scroll + Event.current.delta.y * 32, 0, max); Event.current.Use(); }
            GUI.BeginGroup(view);
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var y = i * rowHeight - _scroll;
                    var title = _live ? LiveTopics[i].Title : Topics[i].Title;
                    var description = _live ? LiveTopics[i].Description : Topics[i].Description;
                    var colors = _live ? LiveTopics[i].Colors : Array.Empty<Color>();
                    for (var c = 0; c < colors.Length; c++)
                    {
                        var swatch = new Rect(c * 26, y + 4, 20, 18);
                        GUI.color = new Color(.14f, .16f, .21f, 1); GUI.DrawTexture(swatch, Texture2D.whiteTexture);
                        GUI.color = colors[c]; GUI.DrawTexture(swatch, Texture2D.whiteTexture);
                    }
                    GUI.color = Color.white;
                    GUI.Label(new Rect(colors.Length * 26, y, view.width - colors.Length * 26, 26), title, _title);
                    GUI.color = new Color(0.74f, 0.8f, 0.9f);
                    GUI.Label(new Rect(0, y + 30, view.width, rowHeight - 40), description, _body);
                    GUI.color = Color.white;
                }
            }
            finally { GUI.EndGroup(); }
            var input = Event.current;
            if (input.type == EventType.MouseDown && input.button == 0 && view.Contains(input.mousePosition))
            {
                GUIUtility.hotControl = _dragControl = drag;
                _dragY = input.mousePosition.y; input.Use();
            }
            else if (_dragControl == drag && GUIUtility.hotControl == drag)
            {
                if (input.type == EventType.MouseDrag)
                {
                    _scroll = Math.Clamp(_scroll + _dragY - input.mousePosition.y, 0, max);
                    _dragY = input.mousePosition.y; input.Use();
                }
                else if (input.type == EventType.MouseUp)
                { GUIUtility.hotControl = 0; _dragControl = 0; input.Use(); }
            }
            if (rect.Contains(Event.current.mousePosition) && Event.current.type is EventType.MouseDown or EventType.MouseUp or EventType.ScrollWheel) Event.current.Use();
        }
        finally { GUI.color = oldColor; }
    }
}

