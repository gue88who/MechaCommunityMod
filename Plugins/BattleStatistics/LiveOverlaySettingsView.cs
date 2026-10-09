using UnityEngine;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal sealed class LiveOverlaySettingsView
{
    internal bool Open { get; private set; }
    private GUIStyle? _title, _label, _small, _button;
    private float _scroll, _dragY;
    private int _dragControl;

    internal void Toggle() { if (Open) Close(); else { Open = true; _scroll = 0; } }
    internal void Close()
    {
        if (_dragControl != 0 && GUIUtility.hotControl == _dragControl) GUIUtility.hotControl = 0;
        _dragControl = 0; Open = false;
    }
    internal void Draw(Rect rect, ResultsSkin skin)
    {
        _title ??= new GUIStyle { font = skin.Title, fontSize = 16, alignment = TextAnchor.MiddleLeft };
        _label ??= new GUIStyle { font = skin.Body, fontSize = 16, alignment = TextAnchor.MiddleLeft };
        _small ??= new GUIStyle { font = skin.Body, fontSize = 14, alignment = TextAnchor.MiddleLeft };
        _button ??= new GUIStyle { font = skin.Body, fontSize = 16, alignment = TextAnchor.MiddleCenter };
        foreach (var style in new[] { _title, _label, _small, _button })
            style.normal.textColor = style.hover.textColor = style.active.textColor = Color.white;
        var oldColor = GUI.color;
        try
        {
            GUI.color = Color.white;
            skin.Art(rect, "replayUI_frame_2");
            skin.Art(new Rect(rect.x + 12, rect.y + 12, rect.width - 66, 40), "button_b_sp1");
            GUI.Label(new Rect(rect.x + 26, rect.y + 20, rect.width - 100, 26), "OVERLAY SETTINGS", _title);
            var close = new Rect(rect.xMax - 44, rect.y + 16, 30, 30);
            skin.Art(close, "button_b_x1");
            if (GUI.Button(close, "X", _button)) { Close(); return; }
            var groups = new[] {
                ("STARTUP", new[] { ("Hide by default", LiveOverlaySettings.HideByDefault) }),
                ("ATTACK", new[] { ("Kills / spell uses", LiveOverlaySettings.Kills), ("Damage", LiveOverlaySettings.Damage),
                    ("Overkill", LiveOverlaySettings.DamageOverkill), ("Hacked", LiveOverlaySettings.Hacked),
                    ("Hack amount", LiveOverlaySettings.HackAmount), ("Failed hack", LiveOverlaySettings.FailedHack),
                    ("Source colors", LiveOverlaySettings.SourceColors) }),
                ("DEFENSE", new[] { ("Tanked", LiveOverlaySettings.Tanked), ("Overkill", LiveOverlaySettings.TankedOverkill) }),
                ("XP", new[] { ("Gained", LiveOverlaySettings.XpGained), ("Fed", LiveOverlaySettings.XpFed) }) };
            var view = new Rect(rect.x + 12, rect.y + 60, rect.width - 24, rect.height - 76);
            var contentHeight = groups.Sum(g => 34 + g.Item2.Length * 32) + 24;
            var max = Math.Max(0, contentHeight - view.height);
            _scroll = Math.Clamp(_scroll, 0, max);
            var drag = GUIUtility.GetControlID(108, FocusType.Passive);
            if (Event.current.type == EventType.ScrollWheel && view.Contains(Event.current.mousePosition))
            { _scroll = Math.Clamp(_scroll + Event.current.delta.y * 32, 0, max); Event.current.Use(); }
            GUI.BeginGroup(view);
            try
            {
                var y = -_scroll;
                foreach (var (name, options) in groups)
                {
                    GUI.color = new Color(.12f, .17f, .24f); GUI.DrawTexture(new Rect(0, y, view.width, 28), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    if (name is "ATTACK" or "DEFENSE") skin.StatIcon(new Rect(8, y + 2, 24, 24), name == "ATTACK");
                    else if (name == "XP") skin.Art(new Rect(8, y + 2, 24, 24), "icon_exp");
                    GUI.Label(new Rect(name == "STARTUP" ? 8 : 40, y + 3, view.width - 48, 24), name, _label);
                    y += 34;
                    foreach (var (label, entry) in options)
                    {
                        GUI.color = new Color(.74f, .8f, .9f);
                        GUI.Label(new Rect(18, y + 4, view.width - 108, 28), label, _label); GUI.color = Color.white;
                        var toggle = new Rect(view.width - 82, y + 2, 72, 28);
                        var hover = GUI.enabled && toggle.Contains(Event.current.mousePosition);
                        skin.Art(toggle, "button_" + (entry.Value ? "y" : "b") + "_x" + (hover ? "2" : "1"));
                        if (GUI.Button(toggle, entry.Value ? "ON" : "OFF", _button))
                        {
                            entry.Value = !entry.Value;
                            RecordingHooks.Observer?.RefreshSettings();
                        }
                        y += 32;
                    }
                    if (name == "STARTUP")
                    {
                        GUI.color = new Color(.65f, .71f, .8f);
                        GUI.Label(new Rect(18, y, view.width - 28, 24), "Applies on the next game launch", _small); GUI.color = Color.white;
                        y += 24;
                    }
                }
            }
            finally { GUI.EndGroup(); }
            var input = Event.current;
            if (input.type == EventType.MouseDown && input.button == 0 && view.Contains(input.mousePosition) && GUIUtility.hotControl == 0)
            { GUIUtility.hotControl = _dragControl = drag; _dragY = input.mousePosition.y; input.Use(); }
            else if (_dragControl == drag && GUIUtility.hotControl == drag)
            {
                if (input.type == EventType.MouseDrag)
                { _scroll = Math.Clamp(_scroll + _dragY - input.mousePosition.y, 0, max); _dragY = input.mousePosition.y; input.Use(); }
                else if (input.type == EventType.MouseUp) { GUIUtility.hotControl = 0; _dragControl = 0; input.Use(); }
            }
            if (rect.Contains(Event.current.mousePosition) && input.type is EventType.MouseDown or EventType.MouseUp or EventType.ScrollWheel) input.Use();
        }
        finally { GUI.color = oldColor; }
    }
}
