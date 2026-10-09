using GameRiver.Fight;
using HarmonyLib;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class EffectOrigins
{
    [ThreadStatic] internal static DamageSource? Context;
    [ThreadStatic] internal static long HitOwner;
    private static readonly Dictionary<long, DamageSource> Buffs = new();
    internal static void Clear() { Buffs.Clear(); Context = null; HitOwner = 0; }
    internal static DamageSource Source(Buff buff) => Buffs.TryGetValue(buff.Pointer.ToInt64(), out var source) ? source : DamageSourceReader.Effect(buff.data);
    private static DamageSource? Known(DamageSource source) => source == DamageSource.Unknown ? null : source;

    // These native performers can create a NormalDamageProvider or a delayed
    // buff, both of which otherwise discard the active technology's identity.
    // Restore the previous scope even if combat throws or effects nest.
    [HarmonyPatch]
    private static class TechnologyHit
    {
        private sealed record Scope(DamageSource? Source, long Owner);
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => new[] {
            typeof(AdditionalDamageProvider), typeof(FireIntensifyEffectProvider),
            typeof(IterationHitEffectProvider), typeof(KillExplosionEffectProvider) }
            .Select(type => AccessTools.Method(type, "GameRiver_Fight_IHitEffectPerformer_PerformHitEffect")
                ?? throw new MissingMethodException(type.FullName, "PerformHitEffect"));
        private static void Prefix(SingleEffectProvider __instance, FightSkill __0, out Scope __state)
        {
            __state = new(Context, HitOwner);
            // A different nested performer must not inherit the outer tech if
            // its own source is unknown (e.g. a native weapon effect).
            Context = null;
            HitOwner = 0;
            try {
                HitOwner = __0.GetOwner()?.Pointer.ToInt64() ?? 0;
                Context = Known(DamageSourceReader.Effect(__instance.GetAvaliableEffectDataSource()));
            }
            catch (Exception error) { BattleStatisticsPlugin.Logger.LogWarning("Technology hit source: " + error.Message); }
        }
        private static void Finalizer(Scope __state) { Context = __state.Source; HitOwner = __state.Owner; }
    }

    // Production and entry happen asynchronously, after CreateMech's context
    // scope has ended. The creator retains the actual spell/technology source.
    [HarmonyPatch]
    private static class ProductionOwner
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => new[] {
            AccessTools.Method(typeof(SupportUnitCreator), nameof(SupportUnitCreator.AddMech)),
            AccessTools.Method(typeof(SupportUnitCreator), nameof(SupportUnitCreator.OnMechEneterFight)) };
        private static void Postfix(SupportUnitCreator __instance, FightMech __0)
        {
            try
            {
                // Enter-fight notifications can reach creators that do not own
                // this mech. Their data source is not its spawning parent.
                if (__instance.mechs is null || !__instance.mechs.Contains(__0)) return;
                var data = __instance.GetDataSource();
                var source = Known(DamageSourceReader.Effect(data?.GetSupportDataSource()))
                    ?? Known(DamageSourceReader.Effect(data?.GetEffectProviderDataSource()));
                SquadTracker.RegisterSpawn(__0, data?.GetParent(), source, data?.GetTeamController());
            }
            catch (Exception error) { BattleStatisticsPlugin.Logger.LogWarning("Support unit ownership: " + error.Message); }
        }
    }

    [HarmonyPatch(typeof(CommanderSkillSubEffectController), nameof(CommanderSkillSubEffectController.PerformHitEffect))]
    private static class SpellEffect
    {
        private static void Prefix(CommanderSkillSubEffectController __instance, out DamageSource? __state)
        {
            __state = Context;
            try { Context = Known(DamageSourceReader.Effect(__instance.commanderSkill)) ?? Context; } catch { }
        }
        private static void Finalizer(DamageSource? __state) => Context = __state;
    }

    [HarmonyPatch(typeof(SupportUnitCreator), nameof(SupportUnitCreator.CreateMech))]
    private static class Production
    {
        private static void Prefix(SupportUnitCreator __instance, out DamageSource? __state)
        {
            __state = Context;
            try
            {
                var data = __instance.unitDataSource;
                Context = Known(DamageSourceReader.Effect(data?.GetEffectProviderDataSource()))
                    ?? Known(DamageSourceReader.Effect(data?.GetSupportDataSource())) ?? Context;
            }
            catch { }
        }
        private static void Finalizer(DamageSource? __state) => Context = __state;
    }
    [HarmonyPatch(typeof(DeadSummonController), nameof(DeadSummonController.PerformDeadEffect))]
    private static class DeathSpawn
    {
        private static void Prefix(IDeadSummon __1, out DamageSource? __state)
        {
            __state = Context;
            try { Context = Known(DamageSourceReader.Effect(__1)) ?? Context; } catch { }
        }
        private static void Finalizer(DamageSource? __state) => Context = __state;
    }
    [HarmonyPatch(typeof(IBEC_DeadSummon), nameof(IBEC_DeadSummon.OnMechDead))]
    private static class BuffSpawn
    {
        private static void Prefix(IBEC_DeadSummon __instance, out DamageSource? __state)
        {
            __state = Context;
            try { if (__instance.buff is { } buff) Context = Known(Source(buff)) ?? Context; } catch { }
        }
        private static void Finalizer(DamageSource? __state) => Context = __state;
    }

    [HarmonyPatch(typeof(BuffSystem), nameof(BuffSystem.DoAddBuff))]
    private static class Applying
    {
        private static void Prefix(IBuffDataSource __0, out DamageSource? __state)
        {
            __state = Context;
            try { Context = Known(DamageSourceReader.Effect(__0)) ?? Context; }
            catch (Exception e) { BattleStatisticsPlugin.Logger.LogWarning("Buff source: " + e.Message); }
        }
        private static void Finalizer(DamageSource? __state) => Context = __state;
    }
    [HarmonyPatch(typeof(BuffCycleController), nameof(BuffCycleController.TriggerBuffOrBuffRangeItemFromSelector))]
    private static class Selected
    {
        private static void Prefix(IEffectBuffDataSource __0, out DamageSource? __state)
        {
            __state = Context;
            try { Context = Known(DamageSourceReader.Effect(__0)) ?? Context; } catch { }
        }
        private static void Finalizer(DamageSource? __state) => Context = __state;
    }
    [HarmonyPatch(typeof(BuffCycleController), nameof(BuffCycleController.TriggerBuffOrBuffRangeItemFromHit))]
    private static class OnHit
    {
        private static void Prefix(IEffectBuffDataSource __0, out DamageSource? __state)
        {
            __state = Context;
            try { Context = Known(DamageSourceReader.Effect(__0)) ?? Context; } catch { }
        }
        private static void Finalizer(DamageSource? __state) => Context = __state;
    }
    [HarmonyPatch(typeof(Buff), nameof(Buff.Init))]
    private static class Created
    {
        private static void Prefix(Buff __instance)
        {
            Buffs.Remove(__instance.Pointer.ToInt64());
            if (MatchSupport.Allowed && Context is { } source) Buffs[__instance.Pointer.ToInt64()] = source;
        }
    }
    [HarmonyPatch(typeof(Buff), nameof(Buff.Reset))]
    private static class Reapplied
    {
        private static void Prefix(Buff __instance)
        {
            Buffs.Remove(__instance.Pointer.ToInt64());
            if (MatchSupport.Allowed && Context is { } source) Buffs[__instance.Pointer.ToInt64()] = source;
        }
    }
}
