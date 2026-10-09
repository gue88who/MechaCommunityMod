using GameRiver;
using GameRiver.Client;
using GameRiver.Fight;
using HarmonyLib;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class ContributionHooks
{
    private static MatchIdentity? _identity;
    private static readonly Dictionary<long, ContributionLedger> Rounds = new();
    internal static UnitKey Key(FightActor actor)
    {
        var recorder = actor.TryCast<IDamageRecorder>()!;
        return new((int)recorder.GetDamageRecorderType(), recorder.GetID());
    }
    internal static ContributionLedger? Current(FightActor actor)
    {
        if (!MatchSupport.Allowed) return null;
        var fight = actor.GetCurrentTeamController()?.fightController;
        var match = MatchClient.Current;
        if (fight is null || match is null || fight.Pointer != match.GetBattleScene()?.battleSystem?.GetFightController()?.Pointer) return null;
        var manager = fight.GetBattleStatisticManager();
        var round = manager?.GetCurrentRoundStatisticData();
        if (manager is null || round is null || !manager.Enabled) return null;
        Begin(new(match.Pointer.ToInt64(), manager.Pointer.ToInt64(), match.StartTimeStamp));
        var id = round.Pointer.ToInt64();
        if (!Rounds.TryGetValue(id, out var ledger)) Rounds[id] = ledger = new();
        return ledger;
    }
    internal static void Begin(MatchIdentity identity)
    {
        if (_identity == identity) return;
        _identity = identity; Rounds.Clear(); EffectOrigins.Clear();
    }
    internal static void Retain(IEnumerable<long> rounds)
    {
        var retained = rounds.ToHashSet();
        foreach (var key in Rounds.Keys.Where(k => !retained.Contains(k)).ToArray()) Rounds.Remove(key);
    }
    internal static void Finish(long round) { if (Rounds.TryGetValue(round, out var ledger)) ledger.Hacks.Finish(); }
    internal static TeamStats Enrich(long round, int team, TeamStats stats)
        => Rounds.TryGetValue(round, out var ledger) ? ledger.Enrich(stats, team) : stats;

    internal static void Hit(IDamageRecorder? source, IDamageRecorder target, long dealt, long taken, long overkill, long kills)
    {
        if (target.TryCast<FightActor>() is { } victim && Current(victim) is { } ledger)
        {
            ledger.Hit(victim.Pointer.ToInt64(), victim.GetCurrentTeamController().GetTeamIndex(), Key(victim), false, taken, overkill);
            if (source?.TryCast<FightActor>() is { } attacker)
                ledger.Hit(attacker.Pointer.ToInt64(), attacker.GetCurrentTeamController().GetTeamIndex(), Key(attacker), true, dealt, overkill, kills);
        }
    }
    internal static void Supplement(IDamageRecorder source, DamageSource effect, long damage, long overkill, long kills)
    {
        SquadTracker.Supplement(source, effect, damage, overkill, kills);
        if (source.TryCast<FightActor>() is { } actor)
            Current(actor)?.Supplement(actor.GetCurrentTeamController().GetTeamIndex(), Key(actor), effect, damage, overkill, kills);
    }
    private static long _nextWarning;
    private static void Warn(Exception e)
    {
        if (Environment.TickCount64 < _nextWarning) return;
        _nextWarning = Environment.TickCount64 + 15000;
        BattleStatisticsPlugin.Logger.LogWarning("Contribution observation: " + e.Message);
    }

    [HarmonyPatch(typeof(TeamTranslationSystem), nameof(TeamTranslationSystem.Translate))]
    private static class Progress
    {
        private static void Prefix(FightMech __0, FightMech __1, int __2)
        {
            try {
                var team = __1.GetCurrentTeamController().GetTeamIndex();
                if (__2 <= 0 || __0.GetCurrentTeamController().GetTeamIndex() == team || Current(__1) is not { } ledger) return;
                // Translate can complete the conversion synchronously. Record its
                // final pulse before ChangeTeam settles the attempt.
                var squad = SquadTracker.ObserveHacker(__1);
                ledger.Hacks.Add(__1.Pointer.ToInt64(), __0.Pointer.ToInt64(), team, Key(__1), __2, squad);
            }
            catch (Exception e) { Warn(e); }
        }
    }
    [HarmonyPatch(typeof(TeamTranslationSystem), nameof(TeamTranslationSystem.Remove))]
    private static class LostTarget
    {
        private static void Prefix(FightMech __0, FightSkill __1)
        {
            try { if (__1.GetOwner()?.TryCast<FightMech>() is { } hacker) Current(hacker)?.Hacks.End(hacker.Pointer.ToInt64(), __0.Pointer.ToInt64()); }
            catch (Exception e) { Warn(e); }
        }
    }
    private sealed record ConversionState(ContributionLedger Ledger, long Target, UnitKey Unit, int Team);

    [HarmonyPatch(typeof(TeamTranslationSystem), nameof(TeamTranslationSystem.ChangeTeam),
        new[] { typeof(FightMech), typeof(FightTeamController) })]
    private static class Converted
    {
        private static void Prefix(FightMech __0, FightTeamController __1, out ConversionState? __state)
        {
            __state = null;
            try
            {
                if (__1 is null || __0.GetCurrentTeamController()?.GetTeamIndex() == __1.GetTeamIndex()) return;
                if (Current(__0) is not { } ledger) return;
                __state = new(ledger, __0.Pointer.ToInt64(), Key(__0), __1.GetTeamIndex());
                ledger.Hacks.BeginConversion(__state.Target);
            }
            catch (Exception e) { Warn(e); }
        }
        private static void Postfix(FightMech __0, ConversionState? __state)
        {
            try
            {
                if (__state is not null && __0.GetCurrentTeamController()?.GetTeamIndex() == __state.Team)
                    if (__state.Ledger.Convert(__state.Target, __state.Unit, __state.Team) is { } owner)
                        SquadTracker.RegisterOwner(__0, owner.Actor);
            }
            catch (Exception e) { Warn(e); }
        }
        private static void Finalizer(ConversionState? __state)
        {
            try { if (__state is not null) __state.Ledger.Hacks.EndConversion(__state.Target); }
            catch (Exception e) { Warn(e); }
        }
    }
    [HarmonyPatch(typeof(FightMech), nameof(FightMech.OnDead))]
    private static class Died
    {
        private static void Prefix(FightMech __instance)
        {
            try { Current(__instance)?.Hacks.Died(__instance.Pointer.ToInt64()); }
            catch (Exception e) { Warn(e); }
        }
    }
    [HarmonyPatch(typeof(SummonSystem), nameof(SummonSystem.AddMech))]
    private static class Spawned
    {
        private static void Postfix(FightMech __0, ISkillOwner __1)
        {
            try
            {
                SquadTracker.RegisterSpawn(__0, __1);
                var parent = __1?.TryCast<FightMech>();
                var ledger = Current(__0);
                ledger?.Spawn(__0.Pointer.ToInt64(), Key(__0), parent is null ? null : Key(parent),
                    __0.GetCurrentTeamController().GetTeamIndex(), EffectOrigins.Context?.Name ?? "");
            }
            catch (Exception e) { Warn(e); }
        }
    }
}
