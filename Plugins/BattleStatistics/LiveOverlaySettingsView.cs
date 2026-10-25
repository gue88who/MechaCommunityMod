using UnityEngine;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal sealed class LiveOverlaySettingsView
{
    internal bool Open { get; private set; }
    private GUIStyle? _title, _label, _small, _button;
    private float _scroll, _dragY;
    private int _dragControl;
    internal static bool BindingHotkey { get; private set; }
    private string? _hotkeyError;

    internal void Toggle() { if (Open) Close(); else { Open = true; _scroll = 0; } }
    internal void Close()
    {
        if (_dragControl != 0 && GUIUtility.hotControl == _dragControl) GUIUtility.hotControl = 0;
        _dragControl = 0; Open = false; BindingHotkey = false;
    }
    internal void Draw(Rect rect, ResultsSkin skin)
    {
        if (BindingHotkey && Event.current.type == EventType.KeyDown && !OverlayTextInput.Active)
        {
            var key = Event.current.keyCode;
            if (key != KeyCode.None)
            {
                if (key != KeyCode.Escape && OverlayHotkey.Conflicts(key))
                {
                    _hotkeyError = $"{key} is used by the game. Choose another key.";
                    Event.current.Use();
                }
                else
                {
                if (key != KeyCode.Escape) BattleStatisticsPlugin.ToggleKey.Value = key;
                BindingHotkey = false;
                _hotkeyError = null;
                RecordingHooks.Observer?.RefreshSettings();
                Event.current.Use();
                }
            }
        }
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
                ("BATTLE", new[] { ("Layout", LiveOverlaySettings.BattleCentered), ("Visibility", LiveOverlaySettings.BattleOnDemand) }),
                ("DEPLOYMENT", new[] { ("Layout", LiveOverlaySettings.DeploymentCentered), ("Visibility", LiveOverlaySettings.DeploymentOnDemand) }),
                ("ATTACK", new[] { ("Kills / spell uses", LiveOverlaySettings.Kills), ("Damage", LiveOverlaySettings.Damage),
                    ("Overkill", LiveOverlaySettings.DamageOverkill), ("Hacked", LiveOverlaySettings.Hacked),
                    ("Hack amount", LiveOverlaySettings.HackAmount), ("Failed hack", LiveOverlaySettings.FailedHack),
                    ("Source colors", LiveOverlaySettings.SourceColors) }),
                ("DEFENSE", new[] { ("Tanked", LiveOverlaySettings.Tanked), ("Overkill", LiveOverlaySettings.TankedOverkill) }),
                ("XP", new[] { ("Gained", LiveOverlaySettings.XpGained), ("Fed", LiveOverlaySettings.XpFed) }) };
            var view = new Rect(rect.x + 12, rect.y + 60, rect.width - 24, rect.height - 76);
            // Pair related sections on wide screens; retain one column on small ones.
            var columns = view.width >= 650 ? 2 : 1;
            var columnWidth = (view.width - (columns - 1) * 16) / columns;
            float GroupHeight(int index) => 34 + groups[index].Item2.Length * 32;
            var contentHeight = 168f;
            for (var index = 0; index < groups.Length; index += columns)
                contentHeight += Enumerable.Range(index, Math.Min(columns, groups.Length - index)).Max(GroupHeight) + 12;
            var max = Math.Max(0, contentHeight - view.height);
            _scroll = Math.Clamp(_scroll, 0, max);
            var drag = GUIUtility.GetControlID(108, FocusType.Passive);
            if (Event.current.type == EventType.ScrollWheel && view.Contains(Event.current.mousePosition))
            { _scroll = Math.Clamp(_scroll + Event.current.delta.y * 32, 0, max); Event.current.Use(); }
            GUI.BeginGroup(view);
            try
            {
                var y = -_scroll;
                GUI.Label(new Rect(18, y + 4, 180, 28), "Overlay hotkey", _label);
                if (GUI.Button(new Rect(view.width - 144, y + 2, 134, 28), BindingHotkey ? "Press a key…" : BattleStatisticsPlugin.ToggleKey.Value.ToString(), _button))
                {
                    BindingHotkey = true;
                    _hotkeyError = null;
                }
                y += 40;
                GUI.Label(new Rect(18, y, view.width - 28, 24), _hotkeyError ?? (OverlayHotkey.Conflicts(BattleStatisticsPlugin.ToggleKey.Value)
                    ? "Hotkey conflicts with game controls; choose another." : "Game hotkeys are reserved. Esc cancels binding."), _small);
                y += 24;
                foreach (var (caption, entry, maximum) in new[] {
                    ("Unit types per page", BattleStatisticsPlugin.MaxRows, 16),
                    ("Per page (spectating)", BattleStatisticsPlugin.SpectatorMaxRows, 24) })
                {
                    GUI.Label(new Rect(18, y + 4, view.width - 140, 28), caption, _label);
                    if (GUI.Button(new Rect(view.width - 122, y + 2, 30, 28), "−", _button))
                    { entry.Value = Math.Max(1, entry.Value - 1); RecordingHooks.Observer?.RefreshSettings(); }
                    GUI.Label(new Rect(view.width - 88, y + 4, 40, 28), entry.Value.ToString(), _button);
                    if (GUI.Button(new Rect(view.width - 44, y + 2, 30, 28), "+", _button))
                    { entry.Value = Math.Min(maximum, entry.Value + 1); RecordingHooks.Observer?.RefreshSettings(); }
                    y += 34;
                }
                GUI.Label(new Rect(18, y, view.width - 28, 24), "Available screen space may reduce this limit.", _small);
                y += 24;
                var rowY = y;
                var rowHeight = 0f;
                for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                {
                    var (name, options) = groups[groupIndex];
                    var column = groupIndex % columns;
                    rowHeight = Math.Max(rowHeight, GroupHeight(groupIndex));
                    GUI.BeginGroup(new Rect(column * (columnWidth + 16), rowY, columnWidth, GroupHeight(groupIndex)));
                    try
                    {
                    y = 0;
                    GUI.color = new Color(.12f, .17f, .24f); GUI.DrawTexture(new Rect(0, y, columnWidth, 28), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    if (name is "ATTACK" or "DEFENSE") skin.StatIcon(new Rect(8, y + 2, 24, 24), name == "ATTACK");
                    else if (name == "XP") skin.Art(new Rect(8, y + 2, 24, 24), "icon_exp");
                    GUI.Label(new Rect(name is "BATTLE" or "DEPLOYMENT" ? 8 : 40, y + 3, columnWidth - 48, 24), name, _label);
                    y += 34;
                    foreach (var (label, entry) in options)
                    {
                        GUI.color = new Color(.74f, .8f, .9f);
                        GUI.Label(new Rect(18, y + 4, columnWidth - 108, 28), label, _label); GUI.color = Color.white;
                        var phase = name is "BATTLE" or "DEPLOYMENT";
                        var caption = phase ? label == "Layout" ? entry.Value ? "Scoreboard" : "Sidebars" : entry.Value ? "Hold" : "Toggle" : entry.Value ? "ON" : "OFF";
                        var toggle = new Rect(columnWidth - (phase ? 144 : 82), y + 2, phase ? 134 : 72, 28);
                        var hover = GUI.enabled && toggle.Contains(Event.current.mousePosition);
                        skin.Art(toggle, "button_" + (entry.Value ? "y" : "b") + "_x" + (hover ? "2" : "1"));
                        if (GUI.Button(toggle, caption, _button))
                        {
                            entry.Value = !entry.Value;
                            RecordingHooks.Observer?.RefreshSettings();
                        }
                        y += 32;
                    }
                    }
                    finally { GUI.EndGroup(); }
                    if (column == columns - 1 || groupIndex == groups.Length - 1)
                    { rowY += rowHeight + 12; rowHeight = 0; }
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
