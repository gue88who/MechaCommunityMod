using GameRiver.Client;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class OverlayTextInput
{
    private static ChatWindow? _chat;
    private static float _chatRetryAt;
    private static GameObject? _selected;
    private static TMP_InputField? _tmpInput;
    private static InputField? _input;
    private static KeyCode _typingKey;

    internal static bool Active
    {
        get
        {
            // Chat's input state also covers the transition into text editing,
            // before its input field has received EventSystem focus.
            if (_chat == null && Time.unscaledTime >= _chatRetryAt)
            {
                _chatRetryAt = Time.unscaledTime + 1f;
                _chat = GRUIManager.Instance?.GetWindow<ChatWindow>();
            }
            if (_chat != null && _chat.isActiveAndEnabled
                && (_chat.IsInputState || _chat.InputField?.isFocused == true)) return true;

            // Inspect only the selected control. Cache its input components so
            // this does not scan the scene or search the hierarchy every frame.
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != _selected)
            {
                _selected = selected;
                _tmpInput = selected == null ? null : selected.GetComponentInParent<TMP_InputField>();
                _input = selected == null ? null : selected.GetComponentInParent<InputField>();
            }
            return (_tmpInput != null && _tmpInput.isActiveAndEnabled && _tmpInput.isFocused)
                || (_input != null && _input.isActiveAndEnabled && _input.isFocused);
        }
    }

    internal static bool AllowsHotkey(KeyCode key)
    {
        var held = Input.GetKey(key);
        if (!held || _typingKey != key) _typingKey = KeyCode.None;
        if (Active)
        {
            if (held) _typingKey = key;
            return false;
        }
        // A key pressed while typing must be released before it can activate
        // hold mode, even if the chat was submitted while that key was held.
        return _typingKey != key;
    }
}
