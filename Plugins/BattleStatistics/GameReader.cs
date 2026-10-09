using GameRiver;
using GameRiver.Client;
using GameRiver.Fight;
using NativeStatsList = Il2CppSystem.Collections.Generic.List<GameRiver.UnitDamageStatisticData>;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal sealed record UnitDisplay(string Name, string Portrait, bool Effect = false, bool DirectPortrait = false,
    SpriteType? PortraitType = null);

internal sealed class GameReader
{
    internal string LeftPortrait { get; private set; } = "";
    internal string RightPortrait { get; private set; } = "";
    internal PlayerAvatars Avatars { get; } = new();
    internal BoardHistory Boards { get; } = new();
    internal bool IsRightTeam(int team) => team == _rightTeamIndex;
    private NativeStatsList _nativeStats = new();
    private readonly Dictionary<UnitKey, UnitDisplay> _display = new();
    private IntPtr _matchPointer;
    internal string? Status { get; private set; }
    internal MatchHistory History { get; } = new();
    internal UnitCostHistory Costs { get; } = new();
    private readonly StatsArchive _archive = new(
        Path.Combine(BepInEx.Paths.GameRootPath, "ProjectDatas", "Stats"), BattleStatisticsPlugin.Version);
    private DateTimeOffset _archiveErrorAfter;
    private readonly OverkillStore _overkill = new();
    private readonly DamageSourceStore _sources = new();
    private readonly LateUnitStats _lateUnits = new();
    private int _leftTeamIndex;
    private int _rightTeamIndex;
    private int _settlementRound;
    private long _settlementNativeRound;
    private long? _leftHpBefore, _rightHpBefore;
    private bool _roundSettled;
    private readonly PlayerHpChanges _hpChanges = new();

    internal OverlaySnapshot? Read(bool archiveOnly = false)
    {
        if (!MatchSupport.Allowed) { Status = null; return null; }
        var match = MatchClient.Current;
        if (match is null || (!match.IsStarted && !match.IsFinished))
        {
            History.End(false);
            Reset(clearDisplay: false);
            return null;
        }
        if (_matchPointer != match.Pointer)
        {
            Reset(clearDisplay: false);
            _matchPointer = match.Pointer;
        }
        // After final capture the scene may be torn down while results remain open.
        // End hooks can still request one explicit archive refresh.
        if (match.IsFinished && History.Finished && !archiveOnly)
            return null;

        // The generated ModuleManager<T>.Get<M>() wrapper throws a TypeLoadException
        // in BepInEx 788. The visible scene already holds the same BattleSystem;
        // read that reference instead. Do not use the global FightController.Current,
        // which may belong to a background AI/forecast simulation.
        var fight = match.GetBattleScene()?.battleSystem?.GetFightController();
        if (fight is null)
        {
            if (match.IsFinished) History.End(true);
            Status = "Waiting for the visible battle scene.";
            return null;
        }
        var teams = fight.GetTeamControllers();
        if (teams is null || teams.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<FightTeamController>>().Count != 2)
        {
            Status = null;
            return null;
        }
        var manager = fight.GetBattleStatisticManager();
        if (manager is null)
            return null;

        var live = match.IsInFightPhase();
        var round = live ? manager.GetCurrentRoundStatisticData() : manager.GetLastRoundStatisticData();
        TeamStats? displayLeft = null, displayRight = null;
        var left = teams[0];
        var right = teams[1];
        // Match the player's own side when playing; spectators use native order.
        var local = match.GetLocalPlayerController();
        if (local is not null && local.GetFightTeamController()?.GetTeamIndex() == right.GetTeamIndex())
            (left, right) = (right, left);

        var available = manager.Enabled;
        Status = available ? null : "Waiting for native combat statistics to start.";
        // Keep sides fixed across local-player switching in training/replays.
        var identity = new MatchIdentity(match.Pointer.ToInt64(), manager.Pointer.ToInt64(), match.StartTimeStamp);
        if (_historyIdentity != identity)
        {
            if (History.Count > 0)
            {
                History.End(false);
                SaveStats(force: true);
            }
            _display.Clear();
            LeftPortrait = RightPortrait = "";
            Avatars.Clear();
            Boards.Clear();
            _historyIdentity = identity;
            _leftTeamIndex = left.GetTeamIndex();
            _rightTeamIndex = right.GetTeamIndex();
        }
        else if (left.GetTeamIndex() != _leftTeamIndex)
            (left, right) = (right, left);

        History.Begin(identity, PlayerName(match, left), PlayerName(match, right));
        var leftInfo = match.GetPlayerManager().GetPlayerController(left)?.GetPlayer()?.GetPortraitInfo();
        var rightInfo = match.GetPlayerManager().GetPlayerController(right)?.GetPlayer()?.GetPortraitInfo();
        LeftPortrait = leftInfo?.GetAvatarURL() ?? leftInfo?.GetPortrait() ?? LeftPortrait;
        RightPortrait = rightInfo?.GetAvatarURL() ?? rightInfo?.GetPortrait() ?? RightPortrait;
        if (string.IsNullOrWhiteSpace(LeftPortrait)) LeftPortrait = leftInfo?.GetPortrait() ?? "";
        if (string.IsNullOrWhiteSpace(RightPortrait)) RightPortrait = rightInfo?.GetPortrait() ?? "";
        NativeAvatarCapture.Capture(LeftPortrait, Avatars);
        NativeAvatarCapture.Capture(RightPortrait, Avatars);
        _ = Avatars.Get(LeftPortrait);
        _ = Avatars.Get(RightPortrait);
        Costs.Begin(identity);
        ContributionHooks.Begin(identity);
        _overkill.Begin(identity);
        _sources.Begin(identity);
        _lateUnits.Begin(identity);
        SquadTracker.Begin(identity);
        SquadTracker.RegisterDeployment();
        SourceDiagnostics.Begin(identity);
        if (available)
        {
            var archive = manager.datas;
            var count = archive?.Count ?? 0;
            if (History.Ended && !match.IsFinished && live) History.Resume(count);
            var acceptArchive = History.SyncArchiveCount(count);
            if (acceptArchive && live && !match.IsFinished && !Costs.Has(count + 1))
                Costs.Put(count + 1, CostHooks.Snapshot(_leftTeamIndex), CostHooks.Snapshot(_rightTeamIndex),
                    CostHooks.Squads(_leftTeamIndex), CostHooks.Squads(_rightTeamIndex));
            for (var i = 0; acceptArchive && i < count; i++)
            {
                if (!History.NeedsArchiveRound(i + 1, count)) continue;
                var saved = archive![i];
                if (saved is not null)
                {
                    var savedLeft = ReadTeam(saved, _leftTeamIndex);
                    var savedRight = ReadTeam(saved, _rightTeamIndex);
                    History.Put(i + 1, savedLeft, savedRight, true);
                    if (saved.Pointer == round?.Pointer)
                        (displayLeft, displayRight) = (savedLeft, savedRight);
                }
            }
            if (live && !match.IsFinished && !archiveOnly)
            {
                var current = manager.GetCurrentRoundStatisticData();
                if (current is not null)
                {
                    var currentLeft = ReadTeam(current, _leftTeamIndex);
                    var currentRight = ReadTeam(current, _rightTeamIndex);
                    if (current.Pointer == round?.Pointer)
                        (displayLeft, displayRight) = (currentLeft, currentRight);
                    if (currentLeft.TotalDamage + currentRight.TotalDamage + currentLeft.TotalDamageTaken
                        + currentRight.TotalDamageTaken + currentLeft.TotalKills + currentRight.TotalKills > 0
                        || currentLeft.Rows.Concat(currentRight.Rows).Any(r => r.Hacking.Total > 0 || r.Casts > 0 || r.XpEarned > 0 || r.EnemyXpAwarded > 0))
                        History.Put(count + 1, currentLeft, currentRight, false);
                }
            }
        }
        if (match.IsFinished)
            History.End(true);

        if (!live && !_roundSettled) FinalizeRoundInfo();
        foreach (var savedRound in History.Rounds.Where(r => r.Complete && r.Info is null))
            if (Winner(savedRound.Number) is { } winner)
                History.SetRoundInfo(savedRound.Number, new(winner, null, null));

        return new OverlaySnapshot(
            History.LeftName, displayLeft ?? ReadTeam(round, _leftTeamIndex),
            History.RightName, displayRight ?? ReadTeam(round, _rightTeamIndex),
            available ? (live ? $"ROUND {match.RoundCount}  /  DAMAGE" : "LAST FIGHT  /  DAMAGE")
                : "STATS UNAVAILABLE", available);
    }

    private MatchIdentity? _historyIdentity;

    internal void SaveStats(bool force = false)
    {
        _archive.DrainMessages((error, message) =>
        {
            if (error) BattleStatisticsPlugin.Logger.LogWarning(message);
            else BattleStatisticsPlugin.Logger.LogInfo(message);
        });
        if (DateTimeOffset.UtcNow < _archiveErrorAfter) return;
        try { _archive.Observe(History, Costs, SavedName, force, Avatars.Get(LeftPortrait), Avatars.Get(RightPortrait), Boards.All()); }
        catch (Exception error)
        {
            _archiveErrorAfter = DateTimeOffset.UtcNow.AddSeconds(30);
            BattleStatisticsPlugin.Logger.LogWarning($"Stats snapshot could not be saved: {error.Message}");
        }
    }

    private string SavedName(UnitKey unit)
    {
        if (_display.TryGetValue(unit, out var display)) return display.Name;
        try { return Describe(unit).Name; }
        catch { return $"Unit {unit.Kind}:{unit.Id}"; }
    }

    internal bool IsVisibleManager(BattleStatisticManager manager) =>
        MatchSupport.Allowed && MatchClient.Current?.GetBattleScene()?.battleSystem?.GetFightController()
            ?.GetBattleStatisticManager()?.Pointer == manager.Pointer;

    internal void CaptureRoundEnd(BattleStatisticManager manager)
    {
        if (!IsVisibleManager(manager) || !manager.Enabled) return;
        if (Read(archiveOnly: true) is null) return;
        var current = manager.GetCurrentRoundStatisticData();
        if (current is not null)
        {
            ContributionHooks.Finish(current.Pointer.ToInt64());
            History.Put((manager.datas?.Count ?? 0) + 1,
                ReadTeam(current, _leftTeamIndex), ReadTeam(current, _rightTeamIndex), true);
        }
    }

    internal void FightStarted()
    {
        if (Read(archiveOnly: true) is null) return;
        EffectCatalog.CheckIcons();
        var manager = MatchClient.Current?.GetBattleScene()?.battleSystem?.GetFightController()?.GetBattleStatisticManager();
        if (manager is not null)
        {
            _settlementRound = (manager.datas?.Count ?? 0) + 1;
            _settlementNativeRound = manager.GetCurrentRoundStatisticData()?.Pointer.ToInt64() ?? 0;
            _leftHpBefore = PlayerHp(_leftTeamIndex);
            _rightHpBefore = PlayerHp(_rightTeamIndex);
            _roundSettled = false;
            _hpChanges.Clear();
            History.Resume(manager.datas?.Count ?? 0);
            Costs.Resume(manager.datas?.Count ?? 0);
            Boards.Resume(manager.datas?.Count ?? 0);
            Boards.Put(BoardCapture.Capture((manager.datas?.Count ?? 0) + 1, _leftTeamIndex, _rightTeamIndex));
            Costs.Put((manager.datas?.Count ?? 0) + 1,
                CostHooks.Snapshot(_leftTeamIndex), CostHooks.Snapshot(_rightTeamIndex),
                CostHooks.Squads(_leftTeamIndex), CostHooks.Squads(_rightTeamIndex));
            var retained = new List<long>();
            var archive = manager.datas;
            for (var i = 0; i < (archive?.Count ?? 0); i++)
                if (archive![i] is { } saved) retained.Add(saved.Pointer.ToInt64());
            _overkill.RetainRounds(retained);
            _sources.RetainRounds(retained);
            _lateUnits.RetainRounds(retained);
            SquadTracker.Retain(retained);
            SquadTracker.RegisterDeployment();
            ContributionHooks.Retain(retained);
        }
    }

    private static PlayerController? PlayerOnTeam(int team)
    {
        var players = MatchClient.Current?.GetPlayerManager()?.playerControllers;
        for (var i = 0; i < (players?.Count ?? 0); i++)
            if (players![i].GetTeamIndex() == team) return players[i];
        return null;
    }
    private static long? PlayerHp(int team)
    {
        var gauge = PlayerOnTeam(team)?.GetTeamController()?.GetScoreGauge();
        return gauge?.TryCast<TeamScoreGaugeReduce>() is null ? null : gauge.GetScore();
    }
    internal void RecordCoreScore(FightMech actor, long score)
    {
        if (_settlementNativeRound != 0 && !_roundSettled)
            SquadTracker.RecordCoreScore(_settlementNativeRound, actor, score);
    }
    internal void RecordPlayerHp(int team, long before, long after)
    {
        if (_settlementRound > 0 && !_roundSettled && _matchPointer == MatchClient.Current?.Pointer
            && (team == _leftTeamIndex || team == _rightTeamIndex)) _hpChanges.Observe(team, before, after);
    }
    private RoundWinner? Winner(int number)
    {
        var results = PlayerOnTeam(_leftTeamIndex)?.GetPlayer()?.RoundFightResultHistory;
        if (results is null || number < 1 || results.Count < number) return null;
        return results[number - 1] switch {
            FightResultType.Win => RoundWinner.Blue, FightResultType.Lose => RoundWinner.Red,
            FightResultType.Deuce => RoundWinner.Draw, _ => RoundWinner.Unknown };
    }
    internal void FinalizeRoundInfo()
    {
        if (_roundSettled || _settlementRound < 1 || History.Find(_settlementRound)?.Complete != true) return;
        if (Winner(_settlementRound) is not { } winner) return;
        var leftAfter = _hpChanges.After(_leftTeamIndex) ?? PlayerHp(_leftTeamIndex);
        var rightAfter = _hpChanges.After(_rightTeamIndex) ?? PlayerHp(_rightTeamIndex);
        long? Loss(long? before, long? after) => before is { } b && after is { } a ? Math.Max(0, b - a) : null;
        var leftLoss = _hpChanges.Loss(_leftTeamIndex) ?? Loss(_leftHpBefore, leftAfter);
        var rightLoss = _hpChanges.Loss(_rightTeamIndex) ?? Loss(_rightHpBefore, rightAfter);
        var leftAssigned = rightLoss is { } rd && SquadTracker.SettleCore(_settlementNativeRound, _leftTeamIndex, rd);
        var rightAssigned = leftLoss is { } ld && SquadTracker.SettleCore(_settlementNativeRound, _rightTeamIndex, ld);
        var archive = MatchClient.Current?.GetBattleScene()?.battleSystem?.GetFightController()?.GetBattleStatisticManager()?.datas;
        if (archive is not null && archive.Count >= _settlementRound && archive[_settlementRound - 1] is { } round)
            History.Put(_settlementRound, ReadTeam(round, _leftTeamIndex), ReadTeam(round, _rightTeamIndex), true);
        History.SetRoundInfo(_settlementRound, new(winner, leftLoss, rightLoss,
            _leftHpBefore, leftAfter, _rightHpBefore, rightAfter,
            leftAssigned ? 0 : rightLoss, rightAssigned ? 0 : leftLoss));
        _roundSettled = true;
        if ((!leftAssigned && rightLoss is > 0) || (!rightAssigned && leftLoss is > 0))
            BattleStatisticsPlugin.Logger.LogInfo($"Round {_settlementRound}: core damage retained without unit attribution (blue {(!leftAssigned ? rightLoss : 0)}, red {(!rightAssigned ? leftLoss : 0)}).");
        SaveStats(force: true);
    }

    internal void RecordLateUnit(BattleStatisticManager manager, IDamageRecorder actor, long damage, long taken, long kills)
    {
        var match = MatchClient.Current;
        if (match is null || !IsVisibleManager(manager) || !manager.Enabled) return;
        var round = manager.GetCurrentRoundStatisticData();
        var recorder = actor.GetDamageRecorder();
        var team = actor.GetCurrentTeamController()?.GetTeamIndex() ?? -1;
        if (round is null || recorder is null || team < 0) return;
        _lateUnits.Begin(new(match.Pointer.ToInt64(), manager.Pointer.ToInt64(), match.StartTimeStamp));
        var key = new UnitKey((int)recorder.GetDamageRecorderType(), recorder.GetID());
        SquadTracker.Begin(new(match.Pointer.ToInt64(), manager.Pointer.ToInt64(), match.StartTimeStamp));
        SquadTracker.ObserveRecorder(round.Pointer.ToInt64(), team, recorder);
        var teams = round.unitDamageStatisticDatasByTeam;
        if (team < teams.Count && teams[team].ContainsKey(recorder)) return;
        _lateUnits.Add(round.Pointer.ToInt64(), team, recorder.Pointer.ToInt64(), key, damage, taken, kills);
    }

    internal void RecordOverkill(BattleStatisticManager manager, RoundStatisticData round, IDamageRecorder recorder, int team, long amount, bool dealt)
    {
        var match = MatchClient.Current;
        if (match is null || !IsVisibleManager(manager)) return;
        _overkill.Begin(new MatchIdentity(match.Pointer.ToInt64(), manager.Pointer.ToInt64(), match.StartTimeStamp));
        _overkill.Add(round.Pointer.ToInt64(), recorder.Pointer.ToInt64(), team, amount, dealt);
    }

    internal void RecordSource(BattleStatisticManager manager, RoundStatisticData round, IDamageRecorder recorder,
        int team, DamageSource source, long damage, long excess, bool dealt, int sourceTeam = -1)
    {
        var match = MatchClient.Current;
        if (match is null || !IsVisibleManager(manager)) return;
        _sources.Begin(new MatchIdentity(match.Pointer.ToInt64(), manager.Pointer.ToInt64(), match.StartTimeStamp));
        _sources.Add(round.Pointer.ToInt64(), recorder.Pointer.ToInt64(), team, dealt, source, damage, excess,
            sourceTeam >= 0 ? IsRightTeam(sourceTeam) : null);
    }

    internal void RecordSpellCast(FightTeamController team, DamageSource source, long release)
    {
        var match = MatchClient.Current;
        var manager = team.fightController?.GetBattleStatisticManager();
        // Native recording may activate on the first damaging hit. A shield,
        // movement ability or an early miss is still a cast before that happens.
        if (match is null || manager is null || !IsVisibleManager(manager)) return;
        var round = manager.GetCurrentRoundStatisticData();
        if (round is null) return;
        _sources.Begin(new(match.Pointer.ToInt64(), manager.Pointer.ToInt64(), match.StartTimeStamp));
        _sources.Cast(round.Pointer.ToInt64(), team.GetTeamIndex(), source, release);
    }

    internal void RecordUnownedSource(BattleStatisticManager manager, RoundStatisticData round,
        int team, DamageSource source, long damage, long excess, long kills)
    {
        var match = MatchClient.Current;
        if (match is null || !IsVisibleManager(manager)) return;
        _sources.Begin(new MatchIdentity(match.Pointer.ToInt64(), manager.Pointer.ToInt64(), match.StartTimeStamp));
        _sources.AddUnowned(round.Pointer.ToInt64(), team, source, damage, excess, kills);
    }

    internal void Finish(GameOverInfo? info)
    {
        if (!MatchSupport.Allowed) return;
        Read(archiveOnly: true);
        string? outcome = null;
        if (info is not null && info.HasWinner)
        {
            if (info.WinnerTeamIndex == _leftTeamIndex) outcome = History.LeftName + " won";
            else if (info.WinnerTeamIndex == _rightTeamIndex) outcome = History.RightName + " won";
        }
        History.End(true, outcome);
    }

    private TeamStats ReadTeam(RoundStatisticData? round, int index)
    {
        var samples = new List<DamageSample>();
        if (round is null)
            return DamageModel.Group(samples);
        // Native GetUnitDatas appends to the supplied list. Always clear it,
        // including between opposing teams and repeated refreshes.
        _nativeStats.Clear();
        round.GetUnitDatas(index, ref _nativeStats);
        var nativeRecorders = new HashSet<long>();
        var squadXp = new HashSet<string>();
        SquadStats Once(SquadStats squad) => squadXp.Add(squad.Id) ? squad : squad with {
            XpEarned = 0, EnemyXpAwarded = 0, CoreDamage = squad.CoreDamage is null ? null : 0 };
        for (var i = 0; i < _nativeStats.Count; i++)
        {
            var data = _nativeStats[i];
            var recorder = data.DamageRecorder;
            if (recorder is null)
                continue;
            if (recorder.GetCurrentTeamController() is { } recordedOwner)
                OwnershipDiagnostics.Report("native-read", round.Pointer.ToInt64(), index,
                    $"{recorder.GetDamageRecorderType()}:{recorder.GetID()}:stored="
                    + (index < round.unitDamageStatisticDatasByTeam.Count
                        && round.unitDamageStatisticDatasByTeam[index].ContainsKey(recorder)), recordedOwner.GetTeamIndex());
            // Native statistics can expose an untouched enemy recorder in both
            // team lists. It is not an observation of this squad on this team.
            if (data.DamageReal == 0 && data.KillCount == 0 && data.DamageTaken == 0
                && recorder.GetCurrentTeamController() is { } owner && owner.GetTeamIndex() != index)
                continue;
            nativeRecorders.Add(recorder.Pointer.ToInt64());
            // IDamageRecorder.GetID is the mech type ID, not an individual model ID.
            // Keep dead recorders: their damage remains part of the round.
            var key = new UnitKey((int)recorder.GetDamageRecorderType(), recorder.GetID());
            Describe(key); // retain names while the game's data is still available
            SourceDiagnostics.Snapshot(round.Pointer.ToInt64(), index, key, true, data.DamageReal,
                _overkill.Get(round.Pointer.ToInt64(), recorder.Pointer.ToInt64(), index, dealt: true),
                _sources.Get(round.Pointer.ToInt64(), recorder.Pointer.ToInt64(), index, true));
            SourceDiagnostics.Snapshot(round.Pointer.ToInt64(), index, key, false, data.DamageTaken,
                _overkill.Get(round.Pointer.ToInt64(), recorder.Pointer.ToInt64(), index),
                _sources.Get(round.Pointer.ToInt64(), recorder.Pointer.ToInt64(), index, false));
            var sample = new DamageSample(key, data.DamageReal, data.KillCount, data.DamageTaken,
                _overkill.Get(round.Pointer.ToInt64(), recorder.Pointer.ToInt64(), index),
                _overkill.Get(round.Pointer.ToInt64(), recorder.Pointer.ToInt64(), index, dealt: true),
                _sources.Get(round.Pointer.ToInt64(), recorder.Pointer.ToInt64(), index, true),
                _sources.Get(round.Pointer.ToInt64(), recorder.Pointer.ToInt64(), index, false));
            samples.Add(sample with { Squads = new[] { Once(SquadTracker.Snapshot(round.Pointer.ToInt64(), index, recorder, sample, Describe(key).Name)) } });
        }
        var roundId = round.Pointer.ToInt64();
        foreach (var (recorder, sample) in _lateUnits.Read(roundId, index, nativeRecorders))
        {
            Describe(sample.Unit);
            var observed = sample with {
                Overkill = _overkill.Get(roundId, recorder, index),
                DealtOverkill = _overkill.Get(roundId, recorder, index, dealt: true),
                DealtSources = _sources.Get(roundId, recorder, index, true),
                TakenSources = _sources.Get(roundId, recorder, index, false) };
            var squad = SquadTracker.SnapshotLate(roundId, index, recorder, observed, Describe(sample.Unit).Name);
            samples.Add(observed with { Squads = squad is null ? null : new[] { Once(squad) } });
        }
        var covered = samples.SelectMany(s => s.Squads ?? Array.Empty<SquadStats>()).Select(s => s.Id).ToHashSet();
        foreach (var (unit, squad) in SquadTracker.XpOnly(roundId, index, covered, key => Describe(key).Name))
            samples.Add(new DamageSample(unit, 0, 0, Squads: new[] { squad }));
        foreach (var effect in _sources.Unowned(round.Pointer.ToInt64(), index))
        {
            var key = new UnitKey(100 + (int)effect.Source.Category, effect.Source.Id);
            var icon = effect.Source.Category switch {
                DamageCategory.Spell => Config.Instance.GetCommanderSkill(effect.Source.Id)?.GetIconName(),
                DamageCategory.Tech => Config.Instance.GetTechnologyByID(effect.Source.Id)?.GetIconName(),
                _ => null };
            _display[key] = new UnitDisplay(effect.Source.Name, icon ?? "", true,
                PortraitType: effect.Source.Category == DamageCategory.Spell ? SpriteType.CommanderSkill : SpriteType.Technology);
            samples.Add(new DamageSample(key, effect.Damage, effect.Kills, DealtOverkill: effect.Overkill,
                DealtSources: new[] { new SourceDamage(effect.Source, effect.Damage, effect.Overkill) }, Casts: effect.Casts));
        }
        return ContributionHooks.Enrich(round.Pointer.ToInt64(), index, DamageModel.Group(samples));
    }

    internal UnitDisplay Describe(UnitKey key)
    {
        if (_display.TryGetValue(key, out var display))
            return display;
        if (key.Kind >= 100) return new UnitDisplay("Unassigned effect", "", true);
        if (key.Kind == (int)DamageRecorderType.Construction)
        {
            var construction = GameRiver.Config.Instance.GetConstructionData(key.Id);
            var portrait = construction?.GetLargePortraitName();
            if (string.IsNullOrWhiteSpace(portrait)) portrait = construction?.GetIconName();
            display = new UnitDisplay(construction?.GetName() ?? $"Construction {key.Id}", portrait ?? "", DirectPortrait: true);
            _display.Add(key, display);
            return display;
        }
        var card = GameRiver.Config.Instance.GetUnitDataByMechID(key.Id);
        if (card is null)
        {
            var name = GameRiver.Config.Instance.GetMechDataByID(key.Id)?.GetName() ?? $"Unit {key.Id}";
            var devices = GameRiver.Config.Instance.GetContraptionDatas();
            var count = devices?.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<ContraptionData>>().Count ?? 0;
            for (var i = 0; i < count; i++)
                if (devices![i]?.GetName() == name)
                {
                    display = new UnitDisplay(name, devices[i].GetIconName(), DirectPortrait: true);
                    _display[key] = display;
                    return display;
                }
            return new UnitDisplay(name, "");
        }
        display = new UnitDisplay(card.GetName(), card.SmallPortraitName ?? "");
        _display.Add(key, display);
        return display;
    }

    private static string PlayerName(MatchClient match, FightTeamController team) =>
        match.GetPlayerManager().GetPlayerController(team)?.GetPlayer()?.GetName()
        ?? $"TEAM {team.GetTeamIndex() + 1}";

    private void Reset(bool clearDisplay = true)
    {
        _matchPointer = IntPtr.Zero;
        _nativeStats.Clear();
        if (clearDisplay) _display.Clear();
        Status = null;
        _settlementRound = 0; _settlementNativeRound = 0; _leftHpBefore = _rightHpBefore = null; _roundSettled = false;
        _hpChanges.Clear();
    }
}
