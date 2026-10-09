using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using GameRiver;
using GameRiver.Client;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace MechaCommunityMod.Plugins.BattleStatistics;

[BepInPlugin(Id, "Mecha Community Mod - Battle Statistics", Version)]
[BepInProcess("Mechabellum.exe")]
public sealed class BattleStatisticsPlugin : BasePlugin
{
    internal const string Id = "mecha.community.mod.battle-statistics";
    internal const string Version = "0.9.100";
    internal static ManualLogSource Logger = null!;
    internal static ConfigEntry<bool> StartVisible = null!;
    internal static ConfigEntry<KeyCode> ToggleKey = null!;
    internal static ConfigEntry<int> MaxRows = null!;
    internal static ConfigEntry<int> SpectatorMaxRows = null!;
    internal static ConfigEntry<float> Scale = null!;
    internal static ConfigEntry<float> Top = null!;
    internal static ConfigEntry<float> RefreshInterval = null!;

    public override void Load()
    {
        var compatibility = GameCompatibility.CheckInstalled(Paths.GameRootPath);
        GameCompatibility.Activate(compatibility, LoadCompatible, message =>
        {
            if (compatibility.Compatible) Log.LogInfo(message);
            else Log.LogWarning(message);
        });
    }

    // Keep game-dependent initialization out of Load's JIT compilation until the check passes.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void LoadCompatible()
    {
        Logger = Log;
        DefaultSettings.Migrate(Config);
        DefaultSettings.Load();
        BattleStatisticsFeatures.Bind(Config);
        StartVisible = Config.Bind("Display", "StartVisible", true, "Legacy startup preference, used only to initialize LiveOverlay.HideByDefault.");
        ToggleKey = Config.Bind("Display", "ToggleKey", KeyCode.F8, "Hide/show both panels.");
        MaxRows = Config.Bind("Display", "MaxRows", (int)DefaultSettings.Number("MaxRows", 8),
            new ConfigDescription("Maximum unit types per page, ordered by damage. Available HUD space may reduce this.", new AcceptableValueRange<int>(1, 16)));
        SpectatorMaxRows = Config.Bind("Display", "SpectatorMaxRows", (int)DefaultSettings.Number("SpectatorMaxRows", 16),
            new ConfigDescription("Maximum unit types per spectator page. Available screen height may reduce this.", new AcceptableValueRange<int>(1, 24)));
        Scale = Config.Bind("Display", "Scale", DefaultSettings.Number("Scale", 1f),
            new ConfigDescription("Panel size relative to a 1920 x 1080 screen.", new AcceptableValueRange<float>(0.6f, 1.5f)));
        Top = Config.Bind("Display", "TopOffset", DefaultSettings.Number("TopOffset", 220f),
            new ConfigDescription("Preferred distance below the top edge in reference pixels, constrained to keep the lower HUD clear.", new AcceptableValueRange<float>(100f, 650f)));
        RefreshInterval = Config.Bind("Display", "RefreshSeconds", DefaultSettings.Number("RefreshSeconds", .25f),
            new ConfigDescription("Statistics refresh interval.", new AcceptableValueRange<float>(0.1f, 2f)));
        DisplaySettings.Bind(Config);
        LiveOverlaySettings.Bind(Config);
        RecordingHooks.Observer = AddComponent<BattleStatisticsBehaviour>();
        var recording = new Harmony(Id + ".recording");
        var installed = 0;
        foreach (var hook in typeof(RecordingHooks).Assembly.GetTypes().Where(t => t.IsDefined(typeof(HarmonyPatch), false)))
        {
            try { recording.CreateClassProcessor(hook).Patch(); installed++; }
            catch (Exception exception) { Log.LogWarning($"Observation hook {hook.FullName} unavailable: {exception}"); }
        }
        Log.LogInfo($"Installed {installed} recording and contribution observation hooks.");
        OverkillHooks.Install();
        Log.LogInfo($"Battle Statistics {Version} loaded. F8 toggles side panels; Stats opens the sortable table; Settings controls saved column visibility.");
    }
}

public sealed class BattleStatisticsBehaviour : MonoBehaviour
{
    internal void RefreshSettings() => _refreshAt = 0;
    private GameReader? _reader;
    private OverlayView? _view;
    private ResultsView? _results;
    private SelectedUnitView? _unitStats;
    private float _refreshAt;
    private float _logAt;
    private string? _status;
    private bool _panelsVisible = !LiveOverlaySettings.HideByDefault.Value;

    public BattleStatisticsBehaviour(IntPtr pointer) : base(pointer) { }

    private void Update()
    {
        try
        {
            if (!MatchSupport.Allowed)
            {
                // Remove buttons, input shields and cached results as well as
                // hiding F8. A previous 1v1 must not leak into another mode.
                _view?.SetVisible(false);
                _results?.Dispose(); _results = null;
                _unitStats?.Dispose(); _unitStats = null;
                if (_reader is not null)
                {
                    _reader.History.End(false);
                    _reader.SaveStats(force: true);
                    _reader = null;
                }
                _status = null; _refreshAt = 0;
                return;
            }
            if (BattleStatisticsFeatures.LiveOverlay.Value && Input.GetKeyDown(BattleStatisticsPlugin.ToggleKey.Value))
            {
                _panelsVisible = !_panelsVisible;
                BattleStatisticsPlugin.Logger.LogInfo($"Battle Statistics {(_panelsVisible ? "shown" : "hidden")} via {BattleStatisticsPlugin.ToggleKey.Value}.");
                _refreshAt = 0;
                _view?.SetVisible(_panelsVisible && _results?.Visible != true);
            }
            if (Input.GetKeyDown(KeyCode.Escape) && _results?.Visible == true && _reader is not null)
                _results.Dismiss();
            if (!BattleStatisticsFeatures.LiveOverlay.Value || !_panelsVisible || _results?.Visible == true)
            {
                _view?.SetVisible(false);
            }
            var now = Time.unscaledTime;
            if (now < _refreshAt)
                return;
            _refreshAt = now + BattleStatisticsPlugin.RefreshInterval.Value;
            using var timing = new SlowOperation("stats refresh");
            _reader ??= new GameReader();
            var snapshot = _reader.Read();
            // Native stats stop refreshing after game over. Preserve F8's side-panel
            // behavior when returning to the battlefield, without leaking into the lobby.
            snapshot ??= StatsPresentation.CompletedBattle(_reader.History,
                MatchClient.Current?.GetBattleScene()?.battleSystem?.GetFightController() is not null);
            _reader.SaveStats();
            if (BattleStatisticsFeatures.PostGameStats.Value && _results is null && _reader.History.Ended && _reader.History.Count > 0)
                _results = new ResultsView();
            _results?.Observe(_reader.History);
            if (BattleStatisticsFeatures.IndividualStats.Value)
            {
                _unitStats ??= new SelectedUnitView();
                _unitStats.Observe(_reader, _results?.Visible == true);
            }
            if (_status != _reader.Status)
            {
                _status = _reader.Status;
                if (_status is not null)
                    BattleStatisticsPlugin.Logger.LogInfo(_status);
            }
            if (snapshot is null || !BattleStatisticsFeatures.LiveOverlay.Value || !_panelsVisible || _results?.Visible == true)
            {
                _view?.SetVisible(false);
                return;
            }
            _view ??= new OverlayView();
            _view.Refresh(snapshot, _reader);
            _view.SetVisible(true);
        }
        catch (Exception exception)
        {
            _view?.SetVisible(false);
            _unitStats?.Close();
            // Loading a scene or a future incompatible game update should not
            // spam the log each frame or interfere with the game's update.
            var now = Time.unscaledTime;
            _refreshAt = now + 2f;
            if (now >= _logAt)
            {
                _logAt = now + 15f;
                BattleStatisticsPlugin.Logger.LogWarning($"Battle Statistics could not refresh: {exception}");
            }
        }
    }

    // Hook failures must not propagate into combat or result-window code.
    [HideFromIl2Cpp]
    internal void StatsWindowShown(GameRiver.Client.GRWindow window)
    {
        if (!MatchSupport.Allowed) return;
        if (!BattleStatisticsFeatures.PostGameStats.Value || _reader is null || !_reader.History.Ended || _reader.History.Count == 0) return;
        _results ??= new ResultsView();
        _results.WindowShown(window, _reader.History);
    }

    [HideFromIl2Cpp]
    internal void SpellCast(GameRiver.Fight.FightTeamController team, DamageSource source, long release)
    {
        if (!MatchSupport.Allowed) return;
        _reader ??= new GameReader();
        _reader.RecordSpellCast(team, source, release);
    }

    [HideFromIl2Cpp]
    internal void LateUnitRecorded(BattleStatisticManager manager, GameRiver.Fight.IDamageRecorder actor, long damage, long taken, long kills)
    {
        if (!MatchSupport.Allowed) return;
        _reader ??= new GameReader();
        _reader.RecordLateUnit(manager, actor, damage, taken, kills);
    }

    [HideFromIl2Cpp]
    internal void OverkillRecorded(BattleStatisticManager manager, RoundStatisticData round,
        GameRiver.Fight.IDamageRecorder recorder, int team, long amount, bool dealt)
    {
        if (!MatchSupport.Allowed) return;
        _reader ??= new GameReader();
        _reader.RecordOverkill(manager, round, recorder, team, amount, dealt);
    }

    [HideFromIl2Cpp]
    internal void SourceRecorded(BattleStatisticManager manager, RoundStatisticData round,
        GameRiver.Fight.IDamageRecorder recorder, int team, DamageSource source, long damage, long excess, bool dealt, int sourceTeam = -1)
    {
        if (!MatchSupport.Allowed) return;
        _reader ??= new GameReader();
        _reader.RecordSource(manager, round, recorder, team, source, damage, excess, dealt, sourceTeam);
    }

    [HideFromIl2Cpp]
    internal void UnownedSourceRecorded(BattleStatisticManager manager, RoundStatisticData round,
        int team, DamageSource source, long damage, long excess, long kills)
    {
        if (!MatchSupport.Allowed) return;
        _reader ??= new GameReader();
        _reader.RecordUnownedSource(manager, round, team, source, damage, excess, kills);
    }

    [HideFromIl2Cpp]
    internal void FightStarted() => Capture(() =>
    {
        _reader ??= new GameReader();
        _reader.FightStarted();
    });

    [HideFromIl2Cpp]
    internal void CoreScoreObserved(GameRiver.Fight.FightMech actor, long score)
    { if (MatchSupport.Allowed) _reader?.RecordCoreScore(actor, score); }

    [HideFromIl2Cpp]
    internal void PlayerHpObserved(int team, long before, long after)
    { if (MatchSupport.Allowed) _reader?.RecordPlayerHp(team, before, after); }

    [HideFromIl2Cpp]
    internal void RoundSettled() => Capture(() =>
    {
        if (_reader is null) return;
        _reader.FinalizeRoundInfo();
    });

    [HideFromIl2Cpp]
    internal void RoundEnding(BattleStatisticManager manager) => Capture(() =>
    {
        _reader ??= new GameReader();
        _reader.CaptureRoundEnd(manager);
    });

    [HideFromIl2Cpp]
    internal void RoundEnded(BattleStatisticManager manager) => Capture(() =>
    {
        _reader ??= new GameReader();
        if (_reader.IsVisibleManager(manager))
        {
            _reader.Read(archiveOnly: true);
            _reader.SaveStats(force: true);
        }
    });

    [HideFromIl2Cpp]
    internal void MatchEnded(GameOverInfo info) => Capture(() =>
    {
        _reader ??= new GameReader();
        _reader.Finish(info);
    });

    [HideFromIl2Cpp]
    private void Capture(Action action)
    {
        if (!MatchSupport.Allowed) return;
        using var timing = new SlowOperation("round capture");
        try { action(); _reader?.SaveStats(); _refreshAt = 0; }
        catch (Exception exception)
        {
            if (Time.unscaledTime >= _logAt)
            {
                _logAt = Time.unscaledTime + 15;
                BattleStatisticsPlugin.Logger.LogWarning($"Round capture could not refresh: {exception}");
            }
        }
    }

    private void OnGUI()
    {
        if (_reader is null || !MatchSupport.Allowed) return;
        try
        {
            // Keep overlay popup controls ahead of changing live table controls.
            if (_results?.Visible != true) _view?.DrawPopup();
            _results?.Draw(_reader.History, _reader); _unitStats?.Draw(_reader);
        }
        catch (Exception exception)
        {
            _results?.Close();
            _unitStats?.Close();
            _view?.ClosePopups();
            BattleStatisticsPlugin.Logger.LogWarning($"Results screen could not render: {exception}");
        }
    }


    private void LateUpdate()
    {
        // Evaluate after native menu transitions, independently of the 4 Hz
        // statistics refresh, including the reinforcement panel's Show toggle.
        try
        {
            if (!MatchSupport.Allowed) { _view?.SetVisible(false); return; }
            _view?.RefreshVisibility();
            _unitStats?.RefreshVisibility(_results?.Visible == true);
        }
        catch { _view?.SetVisible(false); _unitStats?.Close(); }
    }

    private void OnDestroy()
    {
        if (_reader is not null)
        {
            _reader.History.End(false);
            _reader.SaveStats(force: true);
        }
        if (ReferenceEquals(RecordingHooks.Observer, this)) RecordingHooks.Observer = null;
        _results?.Dispose();
        _unitStats?.Dispose();
        _unitStats = null;
        _results = null;
        _view?.Dispose();
        _view = null;
        _reader = null;
    }
}
