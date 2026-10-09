using GameRiver;
using GameRiver.Client;
using GameRiver.Fight;
using HarmonyLib;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// All patches observe only. A scoped hit prevents explosions/retaliation nested
// inside a death callback from attributing their damage to the outer victim.
internal static class OverkillHooks
{
    [ThreadStatic] private static OverkillHit? _current;
    [ThreadStatic] private static DamageNotification? _notification;
    [ThreadStatic] private static GroundFireSource? _groundFire;
    private static long _nextWarning;

    private readonly record struct HealthSample(OverkillHit? Hit, long Target, int Life, int Damage);
    private sealed record DamageNotification(long Manager, long Target, IDamageRecorder? Source, DamageSource Kind,
        int SourceTeam, long Effective, long Kills, IDamageRecorder? FallbackSource = null);
    private sealed record GroundFireSource(long Target, DamageSource Source, IDamageRecorder? Owner = null, int Team = -1);

    internal static void Install()
    {
        var harmony = new Harmony(BattleStatisticsPlugin.Id + ".overkill");
        try
        {
            // Do NOT patch CalculateHitActorDamage(ref HitDamageInfo, int).
            // This loader treats the non-blittable struct ref as an object ref:
            // it reads the first field as an object pointer and writes a pointer
            // back into the struct. Scope its by-value caller instead.
            harmony.Patch(SupportedMethod(typeof(FightCalculator), nameof(FightCalculator.PerformHitTargetEffect)),
                prefix: Method(nameof(BeginHit)), finalizer: Method(nameof(EndHit)));
            harmony.Patch(SupportedMethod(typeof(DeadLineEffectProvider), nameof(DeadLineEffectProvider.PerformPreHitEffect)),
                prefix: Method(nameof(BeginDeadline)), finalizer: Method(nameof(EndHit)));
            harmony.Patch(SupportedMethod(typeof(SupportUnitCreator), nameof(SupportUnitCreator.PerformAirDropDamage)),
                prefix: Method(nameof(BeginAirDrop)), finalizer: Method(nameof(EndHit)));
            harmony.Patch(SupportedMethod(typeof(FightActor), nameof(FightActor.ReduceLife)),
                prefix: Method(nameof(BeforeHealthLoss)), postfix: Method(nameof(AfterHealthLoss)));
            harmony.Patch(SupportedMethod(typeof(FightController), nameof(FightController.OnActorHitted)),
                prefix: Method(nameof(BeginNotification)), finalizer: Method(nameof(EndNotification)));
            harmony.Patch(SupportedMethod(typeof(BattleStatisticManager), nameof(BattleStatisticManager.RecordDamageTakenStatisticData)),
                postfix: Method(nameof(TakenRecorded)));
            harmony.Patch(SupportedMethod(typeof(BattleStatisticManager), nameof(BattleStatisticManager.RecordDamageStatisticData)),
                postfix: Method(nameof(DealtRecorded)));
            harmony.Patch(SupportedMethod(typeof(GroundFireController), nameof(GroundFireController.PerformItemEffect)),
                prefix: Method(nameof(BeginGroundFire)), finalizer: Method(nameof(EndGroundFire)));
            harmony.Patch(SupportedMethod(typeof(IBEC_ChangeCurrentLife), nameof(IBEC_ChangeCurrentLife.Perform)),
                prefix: Method(nameof(BeginBuff)), finalizer: Method(nameof(EndGroundFire)));
            harmony.Patch(SupportedMethod(typeof(IBEC_ChangeLIfe), nameof(IBEC_ChangeLIfe.Update)),
                prefix: Method(nameof(BeginBuff)), finalizer: Method(nameof(EndGroundFire)));
            BattleStatisticsPlugin.Logger.LogInfo("Overkill observation hooks installed using by-value hit scopes.");
        }
        catch (Exception error)
        {
            harmony.UnpatchSelf();
            BattleStatisticsPlugin.Logger.LogWarning($"Overkill observation unavailable: {error}");
        }
    }

    private static HarmonyMethod Method(string name) => new(AccessTools.Method(typeof(OverkillHooks), name));

    private static System.Reflection.MethodInfo SupportedMethod(Type type, string name)
    {
        var method = AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
        foreach (var parameter in method.GetParameters())
        {
            var argument = parameter.ParameterType;
            if (argument.IsByRef && typeof(Il2CppSystem.ValueType).IsAssignableFrom(argument.GetElementType()!))
                throw new NotSupportedException($"This loader cannot safely marshal {method.Name}'s {parameter.Name} argument.");
        }
        return method;
    }

    private static void BeginHit(FightCalculator __instance, HitDamageInfo __0, out OverkillHit? __state)
    {
        __state = _current;
        _current = null;
        try
        {
            ObserveFight(__instance.fightController);
            // Ground fire builds HitDamageInfo with no provider or skill owner.
            // Capture its range-item provenance only for this exact victim/hit.
            if (_current is { } hit && _groundFire is { } fire && __0.damageProvider is null
                && __0.sourceSkillOwner is null && __0.targetActor is { } effectTarget)
            {
                hit.ContextSource = fire.Source;
                // Native area-effect callbacks can recursively hit another model.
                // Bind the scoped effect to this actual hit rather than the first victim.
                hit.ContextTarget = effectTarget.Pointer.ToInt64();
            }
        }
        catch (Exception error) { Warn(error); }
    }

    private static void BeginGroundFire(RangeItem __0, FightMech __1, out GroundFireSource? __state)
    {
        __state = _groundFire;
        _groundFire = null;
        try
        {
            if (__0 is not null && __1 is not null)
            {
                var source = DamageSourceReader.Effect(__0.provider);
                if (source == DamageSource.Unknown)
                    source = new DamageSource(DamageCategory.Other, -11, "Spread ground fire");
                _groundFire = new GroundFireSource(__1.Pointer.ToInt64(), source,
                    __0.provider?.TryCast<Buff>()?.source?.TryCast<IDamageRecorder>(), __0.teamController?.GetTeamIndex() ?? -1);
            }
        }
        catch (Exception error) { Warn(error); }
    }

    private static void EndGroundFire(GroundFireSource? __state) => _groundFire = __state;

    private static void BeginBuff(IndepentBuffEffectController __instance, out GroundFireSource? __state)
    {
        __state = _groundFire;
        _groundFire = null;
        try
        {
            var buff = __instance.buff;
            if (buff?.owner?.TryCast<FightActor>() is { } target)
                _groundFire = new GroundFireSource(target.Pointer.ToInt64(), EffectOrigins.Source(buff), buff.source?.TryCast<IDamageRecorder>(), buff.sourceTeamController?.GetTeamIndex() ?? -1);
        }
        catch (Exception error) { Warn(error); }
    }

    private static void BeginDeadline(DeadLineEffectProvider __instance, FightActor __1, out OverkillHit? __state)
    {
        __state = _current;
        _current = null;
        try
        {
            ObserveFight(__1?.currentTeamController?.fightController);
            if (_current is { } hit && __1 is not null)
            {
                hit.ContextSource = DamageSourceReader.Effect(__instance.GetAvaliableEffectDataSource());
                hit.ContextTarget = __1.Pointer.ToInt64();
            }
        }
        catch (Exception error) { Warn(error); }
    }

    private static void BeginAirDrop(SupportUnitCreator __instance, out OverkillHit? __state)
    {
        __state = _current;
        _current = null;
        try
        {
            ObserveFight(__instance.fightController);
            if (_current is { } hit)
            {
                var data = __instance.unitDataSource;
                var source = DamageSourceReader.Effect(data?.GetEffectProviderDataSource());
                if (source == DamageSource.Unknown) source = DamageSourceReader.Effect(data?.GetSupportDataSource());
                hit.ContextSource = source == DamageSource.Unknown
                    ? new DamageSource(DamageCategory.Other, -10, "Air-drop impact") : source;
            }
        }
        catch (Exception error) { Warn(error); }
    }

    private static void ObserveFight(FightController? fight)
    {
        if (!MatchSupport.Allowed) return;
        if (RecordingHooks.Observer is null || fight is null) return;
        var visible = MatchClient.Current?.GetBattleScene()?.battleSystem?.GetFightController();
        if (visible?.Pointer != fight.Pointer) return;
        var manager = fight.GetBattleStatisticManager();
        if (manager is not null) _current = new OverkillHit(manager.Pointer.ToInt64());
    }

    // A void finalizer restores observation state without handling/changing any
    // exception from the game, including when a hit exits abnormally.
    private static void EndHit(OverkillHit? __state) => _current = __state;

    private static void BeginNotification(FightController __instance, HitDamageInfo __0, out DamageNotification? __state)
    {
        __state = _notification;
        _notification = null;
        try
        {
            if (!MatchSupport.Allowed) return;
            var manager = __instance.GetBattleStatisticManager();
            var target = __0.targetActor;
            var visible = MatchClient.Current?.GetBattleScene()?.battleSystem?.GetFightController();
            if (visible?.Pointer != __instance.Pointer || manager is null || target is null) return;
            // Enabled is set INSIDE the first native RecordDamageStatisticData
            // call. Testing it here discards that hit's context before either
            // statistics postfix runs, losing its source and outgoing overkill.
            // Still observe only the visible fight; never enable native stats.
            // OnActorHitted credits sourceSkillOwner (not the projectile or target).
            // Keep the interface alive only for this notification, including nested callbacks.
            _notification = new DamageNotification(manager.Pointer.ToInt64(), target.Pointer.ToInt64(),
                __0.sourceSkillOwner?.TryCast<IDamageRecorder>(), DamageSource.Unknown,
                __0.sourceTeam?.GetTeamIndex() ?? __0.damageProvider?.GetTeamController()?.GetTeamIndex() ?? -1, Math.Max(0, __0.damageReal),
                __0.damageReal > 0 && !target.IsAlive() ? 1 : 0);
            _notification = _notification with { Kind = DamageSourceReader.Read(__0.damageProvider) };
            _notification = _notification with { Kind = DamageAttribution.Resolve(_notification.Kind,
                _current?.ContextSource, _current?.ContextTarget ?? -1, target.Pointer.ToInt64()) };
            // Buff life changes can notify the fight directly, bypassing BeginHit.
            // Use their scoped origin only for unresolved damage to this victim.
            if (_notification.Kind == DamageSource.Unknown && _groundFire is { } effect)
                _notification = _notification with { Kind = DamageAttribution.Resolve(_notification.Kind,
                    effect.Source, effect.Target, target.Pointer.ToInt64()) };
            _notification = _notification with {
                Kind = DamageAttribution.FillUnknown(_notification.Kind, EffectOrigins.Context,
                    EffectOrigins.HitOwner, __0.sourceSkillOwner?.Pointer.ToInt64() ?? 0) };
            if (_notification.Source is null && _groundFire is { } scoped
                && (scoped.Target == target.Pointer.ToInt64()
                    || (__0.damageProvider is null && _current?.ContextTarget == target.Pointer.ToInt64()
                        && _current.ContextSource == scoped.Source)))
                _notification = _notification with { FallbackSource = _groundFire.Owner,
                    SourceTeam = _notification.SourceTeam >= 0 ? _notification.SourceTeam : _groundFire.Team };
            if (_notification.Kind == DamageSource.Unknown)
                SourceDiagnostics.Unresolved(manager, __0,
                    _groundFire is { } trace ? $"scoped effect {trace.Source.Category}/{trace.Source.Id}/{trace.Source.Name}, matches target={trace.Target == target.Pointer.ToInt64()}"
                        : _current is { } current ? $"hit effect {current.ContextSource}, matches target={current.ContextTarget == target.Pointer.ToInt64()}" : null);
            if (!manager.Enabled)
                SourceDiagnostics.InitialContext(__0, _notification.Kind);
        }
        catch (Exception error) { Warn(error); }
    }

    private static void EndNotification(DamageNotification? __state) => _notification = __state;

    private static void BeforeHealthLoss(FightActor __instance, HitDamageInfo __0, out HealthSample __state)
    {
        __state = default;
        try
        {
            var hit = _current;
            if (hit is null) return;
            var actorManager = __instance.currentTeamController?.fightController?.GetBattleStatisticManager();
            if (actorManager?.Pointer.ToInt64() != hit.Manager) return;
            __state = new HealthSample(hit, __instance.Pointer.ToInt64(), __instance.GetLife(), __0.damage);
        }
        catch (Exception error) { Warn(error); }
    }

    private static void AfterHealthLoss(int __result, HealthSample __state)
    {
        try
        {
            if (__state.Hit is not null)
                __state.Hit.Observe(__state.Target, __state.Life, __state.Damage, __result);
        }
        catch (Exception error) { Warn(error); }
    }

    private static void TakenRecorded(BattleStatisticManager __instance, IDamageRecorder __0, int __1)
    {
        if (!MatchSupport.Allowed) return;
        try
        {
            var hit = _current;
            if (__0 is null || !__instance.Enabled) return;
            RecordingHooks.Observer?.LateUnitRecorded(__instance, __0, 0, __1, 0);
            var excess = hit?.Manager == __instance.Pointer.ToInt64() ? hit.Consume(__0.Pointer.ToInt64(), __1) : 0;
            var round = __instance.GetCurrentRoundStatisticData();
            if (round is null) return;
            if (excess > 0) RecordSide(__instance, round, __0, excess, dealt: false);
            var notification = _notification;
            if (notification?.Manager == __instance.Pointer.ToInt64() && notification.Target == __0.Pointer.ToInt64())
            {
                var countedExcess = notification.Kind.Category == DamageCategory.Spell ? 0 : excess;
                ContributionHooks.Hit(notification.Source ?? notification.FallbackSource, __0, notification.Effective, Math.Max(0, __1 - excess), countedExcess, notification.Kills);
                if (notification.Source is null && notification.FallbackSource is { } fallback)
                    ContributionHooks.Supplement(fallback, notification.Kind, notification.Effective, countedExcess, notification.Kills);
                RecordSource(__instance, round, __0, notification.Kind, Math.Max(0, __1 - excess), excess, false, notification.SourceTeam);
                if (excess > 0 && notification.Source is { } source)
                {
                    RecordSide(__instance, round, source, excess, dealt: true);
                    RecordSource(__instance, round, source, notification.Kind, 0, excess, true);
                }
                if (notification.Source is null && notification.FallbackSource is null && notification.SourceTeam >= 0
                    && notification.Kind.Category is DamageCategory.Tech or DamageCategory.Spell)
                    RecordingHooks.Observer?.UnownedSourceRecorded(__instance, round, notification.SourceTeam,
                        notification.Kind, notification.Effective, excess, notification.Kills);
            }
            else SourceDiagnostics.MissingContext(__instance, __0, false, __1);
        }
        catch (Exception error) { Warn(error); }
    }

    private static void DealtRecorded(BattleStatisticManager __instance, IDamageRecorder __0, int __2, int __3)
    {
        if (!MatchSupport.Allowed) return;
        try
        {
            var notification = _notification;
            if (!__instance.Enabled || __0 is null) return;
            RecordingHooks.Observer?.LateUnitRecorded(__instance, __0, __2, 0, __3);
            if (notification?.Manager != __instance.Pointer.ToInt64() || notification.Source?.Pointer != __0.Pointer)
            {
                SourceDiagnostics.MissingContext(__instance, __0, true, __2);
                return;
            }
            var round = __instance.GetCurrentRoundStatisticData();
            if (round is not null) RecordSource(__instance, round, __0, notification.Kind, __2, 0, true);
        }
        catch (Exception error) { Warn(error); }
    }

    private static void RecordSource(BattleStatisticManager manager, RoundStatisticData round,
        IDamageRecorder actor, DamageSource source, long damage, long excess, bool dealt, int sourceTeam = -1)
    {
        var team = actor.GetCurrentTeamController();
        var recorder = actor.GetDamageRecorder();
        if (team is null || recorder is null) return;
        var index = team.GetTeamIndex();
        var data = round.GetUnitDamageStatistic(index, recorder);
        if (data?.DamageRecorder is { } recorded)
            RecordingHooks.Observer?.SourceRecorded(manager, round, recorded, index, source, damage, excess, dealt, dealt ? index : sourceTeam);
    }

    private static void RecordSide(BattleStatisticManager manager, RoundStatisticData round,
        IDamageRecorder actor, long excess, bool dealt)
    {
        // Both native counters normalize the recorder and use its CURRENT team.
        // Native dealt statistics are already recorded before the taken event.
        var team = actor.GetCurrentTeamController();
        var recorder = actor.GetDamageRecorder();
        if (team is null || recorder is null) return;
        var index = team.GetTeamIndex();
        var data = round.GetUnitDamageStatistic(index, recorder);
        if (data?.DamageRecorder is not { } recorded) return;
        RecordingHooks.Observer?.OverkillRecorded(manager, round, recorded, index, excess, dealt);
    }

    private static void Warn(Exception error)
    {
        var now = Environment.TickCount64;
        if (now < _nextWarning) return;
        _nextWarning = now + 15000;
        BattleStatisticsPlugin.Logger.LogWarning($"Overkill observation failed: {error}");
    }
}
