using GameRiver;
using GameRiver.Fight;
using GameRiver.Client;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime;
using System.Runtime.InteropServices;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Bounded diagnostics: one message per new path/recorder, at most 32 per match.
// Include source types and numeric counters, never player names or native pointers.
internal static class SourceDiagnostics
{
    private static MatchIdentity? _identity;
    private static readonly HashSet<string> Seen = new();
    internal static void Begin(MatchIdentity identity)
    {
        if (_identity == identity) return;
        _identity = identity;
        Seen.Clear();
    }
    internal static void Unresolved(BattleStatisticManager manager, HitDamageInfo hit, string? context = null)
    {
        if (hit.damage <= 0 && hit.damageReal <= 0) return;
        var provider = hit.damageProvider;
        var nested = provider?.TryCast<FightProjectile>()?.dataSource;
        var description = $"provider={TypeName(provider)}, projectileSource={TypeName(nested)}, owner={TypeName(hit.sourceSkillOwner)}, context={context ?? "none"}";
        Report("hit:" + description, $"Unresolved damage source: {description}, damage={hit.damage}, effective={hit.damageReal}; "
            + new System.Diagnostics.StackTrace(1, false));
    }
    internal static void InitialContext(HitDamageInfo hit, DamageSource source)
    {
        var attacker = hit.sourceSkillOwner?.TryCast<IDamageRecorder>();
        var victim = hit.targetActor?.TryCast<IDamageRecorder>();
        Report("initial-context", $"Initial hit context captured before native statistics activation: " +
            $"attacker={attacker?.GetID()}, target={victim?.GetID()}, provider={TypeName(hit.damageProvider)}, " +
            $"source={source.Category}/{source.Id}/{source.Name}, effective={hit.damageReal}.");
    }
    internal static void Snapshot(long round, int team, UnitKey unit, bool dealt, long total, long excess, SourceDamage[] values)
    {
        var damage = Math.Max(0, dealt ? total : total - excess);
        var tracked = values.Sum(v => v.Damage);
        var trackedExcess = values.Sum(v => v.Overkill);
        if (tracked == damage && trackedExcess == excess) return;
        var kind = tracked > damage || trackedExcess > excess ? "mismatch" : "missing events";
        Report($"snapshot:{round}:{team}:{unit}:{dealt}:{kind}",
            $"Source coverage {kind}: team={team}, unit={unit.Id}, channel={(dealt ? "dealt" : "taken")}, " +
            $"native={damage}+{excess} overkill, observed={tracked}+{trackedExcess} overkill.");
    }
    internal static void MissingContext(BattleStatisticManager manager, IDamageRecorder recorder, bool dealt, long amount)
    {
        if (amount <= 0 || MatchClient.Current?.GetBattleScene()?.battleSystem?.GetFightController()
            ?.GetBattleStatisticManager()?.Pointer != manager.Pointer) return;
        var description = $"channel={(dealt ? "dealt" : "taken")}, recorder={TypeName(recorder)}";
        Report("context:" + description, $"Damage statistic without matching hit context: {description}, amount={amount}.");
    }
    private static string TypeName(Il2CppObjectBase? value) => value is null ? "null"
        : Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(IL2CPP.il2cpp_object_get_class(value.Pointer))) ?? "unnamed";
    private static void Report(string key, string message)
    {
        if (Seen.Count >= 32 || !Seen.Add(key)) return;
        BattleStatisticsPlugin.Logger.LogWarning(message);
    }
}
