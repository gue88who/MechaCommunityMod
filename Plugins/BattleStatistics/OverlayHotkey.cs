using GameRiver;
using UnityEngine;
using MechaCommunityMod.Configuration;
using HarmonyLib;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class OverlayHotkey
{
    private static readonly HashSet<KeyCode> Observed = new();
    internal static bool ModifiersHeld => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
        || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)
        || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    private static bool _reportedUnavailable;
    private static readonly HashSet<KeyCode> ReportedConflicts = new();
    private static readonly HashSet<string> ReportedOwners = new();
    private static void DescribeBinding(Il2CppSystem.Action? callback, IEnumerable<KeyCode> keys, string modifiers)
    {
        try
        {
            if (!keys.Contains(KeyCode.Tab)) return;
            var method = callback?.Method;
            var owner = $"{modifiers}: {method?.DeclaringType?.FullName}.{method?.Name}";
            if (ReportedOwners.Add(owner)) BattleStatisticsPlugin.Logger.LogInfo("Native Tab binding: " + owner);
        }
        catch (Exception error) { BattleStatisticsPlugin.Logger.LogWarning("Could not identify Tab callback: " + error.Message); }
    }
    internal static void Observe(IEnumerable<KeyCode> keys)
    {
        foreach (var key in keys) Observed.Add(key);
    }
    internal static bool Conflicts(KeyCode key)
    {
        if (OverlayKeyPolicy.Reserved(key.ToString())) return true;
        if (Observed.Contains(key)) return ReportConflict(key);
        try
        {
            var mappings = GRInputManager.Instance?.keyMappings;
            if (mappings is null) return Unavailable("native shortcut registry is not available");
            foreach (var mapping in mappings.Values)
                if (mapping is not null && mapping.keycodes is { } keys)
                {
                    DescribeBinding(mapping.function, keys, mapping.shortcutKeyType.ToString());
                    if (mapping.shortcutKeyType.ToString() != "Normal") continue;
                    foreach (var registered in keys)
                        Observed.Add(registered);
                }
            return Observed.Contains(key) && ReportConflict(key);
        }
        catch (Exception error) { return Unavailable(error.Message); }
    }
    private static bool ReportConflict(KeyCode key)
    {
        if (ReportedConflicts.Add(key)) BattleStatisticsPlugin.Logger.LogWarning($"Overlay hotkey {key} is registered by the game and is disabled. Choose another key in the launcher.");
        return true;
    }
    private static bool Unavailable(string reason)
    {
        if (!_reportedUnavailable)
        {
            _reportedUnavailable = true;
            BattleStatisticsPlugin.Logger.LogWarning($"Overlay hotkey registry check unavailable: {reason}. Using reserved-key policy and observed registrations.");
        }
        return false;
    }

    [HarmonyPatch]
    private static class RegisteredShortcuts
    {
        private static IEnumerable<MethodBase> TargetMethods() => typeof(GRInputManager).GetMethods()
            .Where(method => method.Name == nameof(GRInputManager.RegisterShortcutKey));
        private static void Prefix(object[] __args)
        {
            // Retain bindings even after their native window/phase unregisters.
            // Never propagate audit failures into a game input registration.
            try
            {
                // Native registrations specify Normal, Ctrl, Alt or Ctrl_Alt.
                // Our binding is a plain key, so modified shortcuts do not collide.
                if (__args.Length < 2 || __args[1]?.ToString() != "Normal") return;
                foreach (var argument in __args)
                {
                    if (argument is Il2CppStructArray<KeyCode> native) { DescribeBinding(__args[0] as Il2CppSystem.Action, native, "Normal"); Observe(native); }
                    else if (argument is KeyCode[] managed) { DescribeBinding(__args[0] as Il2CppSystem.Action, managed, "Normal"); Observe(managed); }
                }
            }
            catch { }
        }
    }
}
