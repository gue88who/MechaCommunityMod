using GameRiver;
using GameRiver.Client;
using GameRiver.Fight;
using FixedMath;
using HarmonyLib;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class SquadTracker
{
    internal sealed record Identity(string Id, string Nickname, UnitKey Unit, bool Pack,
        string? ParentId = null, UnitKey? ParentUnit = null, bool Spawned = false);
    private static MatchIdentity? _identity;
    private static readonly Dictionary<long, Identity> Recorders = new();
    private static readonly Dictionary<(long Round, int Team, long Recorder), Identity> RoundRecorders = new();
    private static readonly Dictionary<string, Identity> Deployed = new();
    private static readonly Dictionary<(long Round, int Team, string Id), Identity> Observed = new();
    private static readonly ExperienceLedger Experience = new();
    private static readonly UnitLevelLedger Levels = new();
    private static readonly Dictionary<(long Round, int Team, string Id), List<SourceDamage>> SupplementSources = new();
    private static readonly Dictionary<(long Round, int Team, string Id), long> SupplementKills = new();
    private static readonly Dictionary<(long Round, int Team, string Id), long> Conversions = new();
    private static readonly Dictionary<(long Round, int Team, long Actor), (string Id, long Score)> CoreScores = new();
    private static readonly Dictionary<(long Round, int Team), Dictionary<string, long>> SettledCore = new();
    private static int _next, _nextSpawn;
    [ThreadStatic] private static (long Round, int Team, Identity Squad)? _victim;
    [ThreadStatic] private static HashSet<long>? _awarding;
    private static long _warningAfter;

    internal static void Begin(MatchIdentity identity)
    {
        if (_identity == identity) return;
        _identity = identity; Recorders.Clear(); RoundRecorders.Clear(); Deployed.Clear(); Observed.Clear(); Experience.Clear();
        OwnershipDiagnostics.Clear();
        CoreScores.Clear(); SettledCore.Clear(); Levels.Clear();
        SupplementSources.Clear(); SupplementKills.Clear(); Conversions.Clear(); _next = _nextSpawn = 0; _victim = null;
        _awarding?.Clear();
    }
    private static (BattleStatisticManager Manager, long Round)? Current(FightTeamController? team)
    {
        if (!MatchSupport.Allowed) return null;
        var match = MatchClient.Current;
        var fight = team?.fightController;
        if (match is null || fight is null || fight.Pointer != match.GetBattleScene()?.battleSystem?.GetFightController()?.Pointer) return null;
        var manager = fight.GetBattleStatisticManager();
        var round = manager?.GetCurrentRoundStatisticData();
        if (manager is null || round is null) return null;
        Begin(new(match.Pointer.ToInt64(), manager.Pointer.ToInt64(), match.StartTimeStamp));
        return (manager, round.Pointer.ToInt64());
    }
    internal static void RegisterDeployment()
    {
        using var timing = new SlowOperation("squad registration");
        if (!MatchSupport.Allowed) return;
        var match = MatchClient.Current;
        var live = match?.IsInFightPhase() == true;
        var players = match?.GetPlayerManager()?.playerControllers;
        if (players is null) return;
        for (var p = 0; p < players.Count; p++)
        {
            var player = players[p]; var manager = player.GetUnitManager();
            var team = player.GetFightTeamController()?.GetTeamIndex() ?? -1;
            if (team < 0) continue;
            var current = live ? Current(player.GetFightTeamController()) : null;
            var units = manager.units;
            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i]; var meches = unit.GetMechTeam();
                if (meches is null) continue;
                var id = $"deployed:{team}:{manager.GetUnitIndex(unit)}";
                var key = new UnitKey((int)DamageRecorderType.Mech, unit.GetUnitData().GetMechID());
                if (!Deployed.TryGetValue(id, out var identity))
                    Deployed[id] = identity = new(id, SquadNames.At(_next++), key, unit.GetMechCount() > 1);
                Recorders[meches.Pointer.ToInt64()] = identity;
                if (current is { } fight && meches.GetCurrentTeamController()?.GetTeamIndex() == team)
                    CaptureLevel(fight.Round, team, identity, meches);
                var actors = unit.GetFightActors();
                var actorCount = actors.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<FightActor>>().Count;
                for (var a = 0; a < actorCount; a++)
                    if (actors[a]?.TryCast<IDamageRecorder>()?.GetDamageRecorder() is { } recorder)
                        Recorders[recorder.Pointer.ToInt64()] = identity;
            }
        }
    }
    internal static Identity Identify(IDamageRecorder recorder)
    {
        var pointer = recorder.Pointer.ToInt64();
        if (recorder.GetDamageRecorderType() == DamageRecorderType.Construction)
        {
            var team = recorder.GetCurrentTeamController()?.GetTeamIndex() ?? -1;
            var building = new Identity($"construction:{team}:{recorder.GetID()}", "",
                new((int)DamageRecorderType.Construction, recorder.GetID()), false);
            Recorders[pointer] = building;
            return building;
        }
        if (Recorders.TryGetValue(pointer, out var identity)) return identity;
        RegisterDeployment();
        if (Recorders.TryGetValue(pointer, out identity)) return identity;
        var mechTeam = recorder.TryCast<MechTeam>();
        var count = mechTeam?.GetMeches()?.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<FightMech>>().Count ?? 1;
        var index = _next++;
        identity = new("temporary:" + index, SquadNames.At(index), new((int)recorder.GetDamageRecorderType(), recorder.GetID()), count > 1);
        Recorders[pointer] = identity;
        return identity;
    }
    internal static void RegisterSpawn(FightMech child, ISkillOwner? owner, DamageSource? source = null,
        FightTeamController? productionTeam = null)
    {
        var team = productionTeam ?? child.GetCurrentTeamController();
        if (Current(team) is not { } current || child.TryCast<IDamageRecorder>() is not { } actorRecorder) return;
        var recorder = actorRecorder.GetDamageRecorder() ?? actorRecorder;
        var parentRecorder = owner?.TryCast<IDamageRecorder>() ?? owner?.GetMechTeam()?.TryCast<IDamageRecorder>();
        var parent = parentRecorder is null ? null : Identify(parentRecorder.GetDamageRecorder() ?? parentRecorder);
        var spell = (source ?? EffectOrigins.Context) is { Category: DamageCategory.Spell } origin
            ? new UnitKey(100 + (int)DamageCategory.Spell, origin.Id) : (UnitKey?)null;
        if (spell is not null) parent = null;
        var unit = new UnitKey((int)recorder.GetDamageRecorderType(), recorder.GetID());
        var identity = Recorders.TryGetValue(actorRecorder.Pointer.ToInt64(), out var known)
            && known.Spawned && known.Unit == unit ? known with {
                ParentId = parent?.Id ?? (spell is { } spellUnit ? $"type:{spellUnit}" : known.ParentId),
                ParentUnit = parent?.Unit ?? spell ?? known.ParentUnit }
            : new Identity("spawned:" + _nextSpawn++, "", unit, false,
                parent?.Id ?? (spell is { } spellKey ? $"type:{spellKey}" : null), parent?.Unit ?? spell, true);
        Recorders[actorRecorder.Pointer.ToInt64()] = identity;
        // Some effects inherit their parent's recorder; never replace its deployed identity.
        if (parent is null || !Recorders.TryGetValue(recorder.Pointer.ToInt64(), out var existing) || existing.Id != parent.Id)
            Recorders[recorder.Pointer.ToInt64()] = identity;
        Observe(current.Round, team.GetTeamIndex(), identity);
        CaptureLevel(current.Round, team.GetTeamIndex(), identity, child.GetMechTeam());
        RoundRecorders[(current.Round, team.GetTeamIndex(), actorRecorder.Pointer.ToInt64())] = identity;
        if (parent is null || recorder.Pointer != parentRecorder?.Pointer)
            RoundRecorders[(current.Round, team.GetTeamIndex(), recorder.Pointer.ToInt64())] = identity;
        if (parent is not null && parentRecorder?.GetCurrentTeamController() is { } parentTeam)
            Observe(current.Round, parentTeam.GetTeamIndex(), parent);
    }
    internal static SquadStats Snapshot(long round, int team, IDamageRecorder recorder, DamageSample sample, string type)
    {
        var identity = RoundRecorders.TryGetValue((round, team, recorder.Pointer.ToInt64()), out var captured) ? captured : Identify(recorder);
        if (MatchClient.Current?.IsInFightPhase() == true && Current(recorder.GetCurrentTeamController()) is { } current
            && current.Round == round && recorder.GetCurrentTeamController()?.GetTeamIndex() == team)
            CaptureLevel(round, team, identity, recorder.TryCast<MechTeam>());
        return Snapshot(round, team, identity, sample, type);
    }
    private static void CaptureLevel(long round, int team, Identity identity, MechTeam? meches)
    {
        if (meches is null || identity.Unit.Kind != (int)DamageRecorderType.Mech) return;
        try
        {
            // An effect can share its parent's damage recorder. That is not an
            // observation of the child's own level or XP threshold.
            if (meches.GetID() != identity.Unit.Id) return;
            var manager = MatchClient.Current?.GetBattleScene()?.battleSystem?.GetFightController()?.GetBattleStatisticManager();
            if (manager is null || !manager.Enabled || manager.GetCurrentRoundStatisticData()?.Pointer.ToInt64() != round) return;
            // CardLevel's numeric representation is native; its names give the displayed level.
            var level = meches.GetLevel().ToString();
            if (!level.StartsWith("Level", StringComparison.Ordinal) || !int.TryParse(level[5..], out var number)) return;
            Levels.Put(round, team, identity.Id, new((manager.datas?.Count ?? 0) + 1, number,
                FPoint.ToDouble(meches.GetExp()), FPoint.ToDouble(meches.maxExpFloat), meches.IsMaxLevel()));
            Observe(round, team, identity);
        }
        catch (Exception e) { Warn(e); }
    }
    internal static SquadStats? SnapshotLate(long round, int team, long recorder, DamageSample sample, string type) =>
        RoundRecorders.TryGetValue((round, team, recorder), out var identity) || Recorders.TryGetValue(recorder, out identity)
            ? Snapshot(round, team, identity, sample, type) : null;
    internal static void ObserveRecorder(long round, int team, IDamageRecorder recorder)
        => RoundRecorders[(round, team, recorder.Pointer.ToInt64())] = Identify(recorder);
    internal static void ObserveActor(FightActor actor)
    {
        if (actor.TryCast<IDamageRecorder>()?.GetDamageRecorder() is { } recorder)
            Recorders[actor.Pointer.ToInt64()] = Identify(recorder);
    }
    internal static string? ObserveHacker(FightMech actor)
    {
        var team = actor.GetCurrentTeamController();
        if (Current(team) is not { } current || actor.TryCast<IDamageRecorder>() is not { } source) return null;
        var recorder = source.GetDamageRecorder() ?? source;
        var identity = Identify(recorder); var index = team.GetTeamIndex();
        Recorders[actor.Pointer.ToInt64()] = identity;
        RoundRecorders[(current.Round, index, actor.Pointer.ToInt64())] = identity;
        RoundRecorders[(current.Round, index, recorder.Pointer.ToInt64())] = identity;
        Observe(current.Round, index, identity);
        return identity.Id;
    }
    internal static string? SelectedId(Mech mech)
    {
        var card = mech.GetParentCard(); var owner = mech.GetOwner();
        if (card is not null && owner is not null && owner.GetUnitManager().HasUnit(card))
            return $"deployed:{owner.GetFightTeamController().GetTeamIndex()}:{owner.GetUnitManager().GetUnitIndex(card)}";
        if (mech.fightMech?.TryCast<IDamageRecorder>() is { } actor)
            return Identify(actor.GetDamageRecorder() ?? actor).Id;
        return card is null || owner is null ? null
            : $"deployed:{owner.GetFightTeamController().GetTeamIndex()}:{owner.GetUnitManager().GetUnitIndex(card)}";
    }
    internal static void RegisterOwner(FightMech child, long owner)
    {
        if (!Recorders.TryGetValue(owner, out var parent) || child.TryCast<IDamageRecorder>()?.GetDamageRecorder() is not { } recorder) return;
        var identity = Identify(recorder) with { ParentId = parent.Id, ParentUnit = parent.Unit };
        var team = child.GetCurrentTeamController();
        if (Current(team) is { } current)
        {
            var key = (current.Round, team.GetTeamIndex(), parent.Id);
            Conversions[key] = Conversions.GetValueOrDefault(key) + 1;
            Observe(current.Round, team.GetTeamIndex(), parent);
            CaptureLevel(current.Round, team.GetTeamIndex(), identity, child.GetMechTeam());
        }
        Recorders[recorder.Pointer.ToInt64()] = identity;
        Recorders[child.Pointer.ToInt64()] = identity;
    }
    private static SquadStats Snapshot(long round, int team, Identity identity, DamageSample sample, string type)
    {
        Observe(round, team, identity);
        var values = Values(identity, round, team, type);
        return values with { Damage = sample.Damage + values.Damage, Kills = sample.Kills + values.Kills,
            Taken = sample.DamageTaken, Overkill = sample.DealtOverkill + values.Overkill, TakenOverkill = sample.Overkill,
            DealtSources = DamageSources.Merge((sample.DealtSources ?? Array.Empty<SourceDamage>()).Concat(values.DealtSources ?? Array.Empty<SourceDamage>())),
            TakenSources = sample.TakenSources };
    }
    private static void Observe(long round, int team, Identity identity)
    {
        var parts = identity.Id.Split(':');
        if (parts.Length == 3 && parts[0] == "deployed" && int.TryParse(parts[1], out var owner))
            OwnershipDiagnostics.Report("managed-observe", round, team, identity.Id, owner);
        Observed[(round, team, identity.Id)] = identity;
    }
    private static SquadStats Values(Identity identity, long round, int team, string type)
    {
        var xp = Experience.Get(round, team, identity.Id);
        var key = (round, team, identity.Id);
        var sources = DamageSources.Merge(SupplementSources.GetValueOrDefault(key) ?? new());
        return new(identity.Id, identity.Spawned || identity.Unit.Kind == (int)DamageRecorderType.Construction
            ? type : SquadNames.Label(type, identity.Nickname, identity.Pack),
            Damage: sources.Sum(s => s.Damage), Overkill: sources.Sum(s => s.Overkill), Kills: SupplementKills.GetValueOrDefault(key),
            XpEarned: xp.Earned, EnemyXpAwarded: xp.Awarded, DealtSources: sources,
            ParentId: identity.ParentId, ParentUnit: identity.ParentUnit, Spawned: identity.Spawned,
            Hacked: Conversions.GetValueOrDefault(key),
            CoreDamage: SettledCore.TryGetValue((round, team), out var core) ? core.GetValueOrDefault(identity.Id) : null,
            Levels: Levels.Get(round, team, identity.Id));
    }
    internal static void RecordCoreScore(long round, FightMech actor, long score)
    {
        var team = actor.GetCurrentTeamController();
        if (Current(team) is null || actor.TryCast<IDamageRecorder>() is not { } source) return;
        var recorder = source.GetDamageRecorder() ?? source;
        var index = team.GetTeamIndex();
        var identity = RoundRecorders.GetValueOrDefault((round, index, actor.Pointer.ToInt64()))
            ?? RoundRecorders.GetValueOrDefault((round, index, recorder.Pointer.ToInt64())) ?? Identify(recorder);
        Observe(round, index, identity);
        CoreScores[(round, index, actor.Pointer.ToInt64())] = (identity.Id, score);
    }
    internal static bool SettleCore(long round, int team, long damage)
    {
        var scores = CoreScores.Where(p => p.Key.Round == round && p.Key.Team == team).Select(p => p.Value);
        // No core damage means every unit contributed zero, even on the losing side.
        var result = damage == 0 ? new Dictionary<string, long>() : CoreDamageAttribution.Attribute(scores, damage);
        if (result is null) { SettledCore.Remove((round, team)); return false; }
        SettledCore[(round, team)] = result; return true;
    }
    internal static void Supplement(IDamageRecorder source, DamageSource effect, long damage, long overkill, long kills)
    {
        try
        {
            var team = source.GetCurrentTeamController();
            if (Current(team) is not { } current || source.GetDamageRecorder() is not { } recorder) return;
            var identity = Identify(recorder); var key = (current.Round, team.GetTeamIndex(), identity.Id);
            Observe(current.Round, key.Item2, identity);
            if (!SupplementSources.TryGetValue(key, out var list)) SupplementSources[key] = list = new();
            var merged = DamageSources.Merge(list.Append(new SourceDamage(effect, damage, overkill)));
            list.Clear(); list.AddRange(merged);
            SupplementKills[key] = SupplementKills.GetValueOrDefault(key) + kills;
        }
        catch (Exception e) { Warn(e); }
    }
    internal static IEnumerable<(UnitKey Unit, SquadStats Stats)> XpOnly(long round, int team, HashSet<string> covered, Func<UnitKey, string> describe) =>
        Observed.Where(p => p.Key.Round == round && p.Key.Team == team && !covered.Contains(p.Key.Id))
            .Select(p => (p.Value.Unit, Values(p.Value, round, team, describe(p.Value.Unit))));
    internal static void Retain(IEnumerable<long> rounds)
    {
        var keep = rounds.ToHashSet(); Experience.Retain(keep); Levels.Retain(keep);
        foreach (var key in RoundRecorders.Keys.Where(k => !keep.Contains(k.Round)).ToArray()) RoundRecorders.Remove(key);
        foreach (var key in Observed.Keys.Where(k => !keep.Contains(k.Round)).ToArray()) Observed.Remove(key);
        foreach (var key in SupplementSources.Keys.Where(k => !keep.Contains(k.Round)).ToArray()) SupplementSources.Remove(key);
        foreach (var key in SupplementKills.Keys.Where(k => !keep.Contains(k.Round)).ToArray()) SupplementKills.Remove(key);
        foreach (var key in Conversions.Keys.Where(k => !keep.Contains(k.Round)).ToArray()) Conversions.Remove(key);
        foreach (var key in CoreScores.Keys.Where(k => !keep.Contains(k.Round)).ToArray()) CoreScores.Remove(key);
        foreach (var key in SettledCore.Keys.Where(k => !keep.Contains(k.Round)).ToArray()) SettledCore.Remove(key);
        // Temporary recorders may be pooled and reused after a replay seek.
        foreach (var key in Recorders.Where(p => p.Value.Id.StartsWith("temporary:") || p.Value.Spawned).Select(p => p.Key).ToArray()) Recorders.Remove(key);
    }
    private static void Warn(Exception error)
    {
        if (Environment.TickCount64 < _warningAfter) return;
        _warningAfter = Environment.TickCount64 + 15000;
        BattleStatisticsPlugin.Logger.LogWarning("XP/squad observation: " + error.Message);
    }

    // Track actual squad XP changes after modifiers/caps. No guessed kill-XP formula.
    [HarmonyPatch]
    private static class Award
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => new[] {
            AccessTools.Method(typeof(MechTeam), nameof(MechTeam.AddExp), new[] { typeof(FPoint), typeof(FightMech) }),
            AccessTools.Method(typeof(MechTeam), nameof(MechTeam.AddExp), new[] { typeof(FPoint), typeof(FPoint), typeof(FPoint) }) };
        private sealed record State(long Recorder, long Round, int Team, Identity Squad, double Before);
        private static void Prefix(MechTeam __instance, out State? __state)
        {
            __state = null;
            try
            {
                var pointer = __instance.Pointer.ToInt64();
                _awarding ??= new();
                if (_awarding.Contains(pointer)) return;
                var team = __instance.GetCurrentTeamController();
                if (Current(team) is not { } current) return;
                var identity = Identify(__instance.Cast<IDamageRecorder>());
                __state = new(pointer, current.Round, team.GetTeamIndex(), identity, FPoint.ToDouble(__instance.GetExp()));
                _awarding.Add(pointer);
            }
            catch (Exception e) { Warn(e); }
        }
        private static void Finalizer(State? __state)
        {
            if (__state is not null) _awarding?.Remove(__state.Recorder);
        }
        private static void Postfix(MechTeam __instance, State? __state)
        {
            if (__state is null) return;
            try
            {
                CaptureLevel(__state.Round, __state.Team, __state.Squad, __instance);
                var earned = Math.Max(0, FPoint.ToDouble(__instance.GetExp()) - __state.Before);
                if (earned <= 0) return;
                Observe(__state.Round, __state.Team, __state.Squad);
                Experience.Add(__state.Round, __state.Team, __state.Squad.Id, earned, 0);
                if (_victim is { } victim && victim.Round == __state.Round && victim.Team != __state.Team)
                {
                    Observe(victim.Round, victim.Team, victim.Squad);
                    Experience.Add(victim.Round, victim.Team, victim.Squad.Id, 0, earned);
                }
            }
            catch (Exception e) { Warn(e); }
        }
    }
    [HarmonyPatch(typeof(ExpSystem), nameof(ExpSystem.CalculateExp))]
    private static class Victim
    {
        internal static void Prefix(FightActor __0, out (long Round, int Team, Identity Squad)? __state)
        {
            __state = _victim; _victim = null;
            try
            {
                var team = __0.GetCurrentTeamController();
                if (Current(team) is not { } current || __0.TryCast<IDamageRecorder>()?.GetDamageRecorder() is not { } recorder) return;
                _victim = (current.Round, team.GetTeamIndex(), Identify(recorder));
            }
            catch (Exception e) { Warn(e); }
        }
        private static void Finalizer((long Round, int Team, Identity Squad)? __state) => _victim = __state;
    }
    [HarmonyPatch(typeof(ExpSystem), nameof(ExpSystem.DoCalculateExp))]
    private static class DirectVictim
    {
        private static void Prefix(FightActor __2, out (long Round, int Team, Identity Squad)? __state) => Victim.Prefix(__2, out __state);
        private static void Finalizer((long Round, int Team, Identity Squad)? __state) => _victim = __state;
    }
}
