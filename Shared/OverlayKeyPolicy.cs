namespace MechaCommunityMod.Configuration;

// Letter/function candidates require an additional native shortcut check.
// These exclusions are known game controls, not a complete native-code audit.
internal static class OverlayKeyPolicy
{
    internal static bool Reserved(string key)
    {
        if (key is "T" or "Tab") return true;
        if (key.Length == 1 && key[0] >= 'A' && key[0] <= 'Z') return false;
        return !(key.StartsWith("F", StringComparison.Ordinal) && int.TryParse(key.Substring(1), out var number)
            && number >= 1 && number <= 15 && key == "F" + number);
    }
}
