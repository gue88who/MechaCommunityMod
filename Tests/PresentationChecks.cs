using MechaCommunityMod.Plugins.BattleStatistics;

internal static class PresentationChecks
{
    internal static readonly (string, Action)[] All = {
        ("Stats stay disabled across unsupported modes and resume for 1v1", MatchSupportChecks.Transitions),
        ("Scopes isolate displayed metrics and respect hidden parameters", Scopes),
        ("XP graphs retain fractional earned and awarded values independently", Experience),
        ("Completed battlefield overlay uses the last fight and never leaks to the lobby", Overlay),
        ("Scope and graph selection preserve recorded damage and XP", Immutable),
        ("Fractional bars remain finite and bounded", Bars),
        ("Only visible native foreground windows and tooltip panels suppress the F8 side overlay", Windows),
        ("F8 pagination reserves lower HUD space only where its final lane overlaps controls", HudPlacement),
        ("Selected-unit stats use available width without crossing right HUD controls", SelectedPlacement)
    };
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void SelectedPlacement()
    {
        var native = new HudBounds(8, 780, 420, 1072);
        var right = new HudBounds(1650, 600, 1912, 1072);
        var beside = SideHudLayout.SelectedUnit(new[] { right }, native, 1920, 1080, 1800, 292);
        Check(beside is { Left: 428, Right: 1642 }, "Popup crossed the right HUD or wasted available width");
        var narrow = new HudBounds(1000, 780, 1912, 1072);
        var constrained = SideHudLayout.SelectedUnit(new[] { narrow }, native, 1920, 1080, 1800, 292);
        Check(constrained is { Left: 428, Top: 780, Right: 992, Bottom: 1072 },
            "Popup must stay beside the card even when more width is available above");
        var obstruction = new HudBounds(600, 780, 700, 1072);
        Check(SideHudLayout.SelectedUnit(new[] { obstruction }, native, 1920, 1080, 1800, 292) is null,
            "Popup must not jump above the card or past an obstruction");
        var blocked = SideHudLayout.SelectedUnit(new[] { new HudBounds(0, 0, 1920, 1080) }, native, 1920, 1080, 1800, 292);
        Check(blocked is null, "Popup must not overlap HUD when no usable space remains");
    }
    private static MatchHistory History()
    {
        var h = new MatchHistory(); h.Begin(new(1, 2, 3), "Left", "Right");
        var key = new UnitKey(0, 5);
        for (var round = 1; round <= 2; round++)
        {
            var left = DamageModel.Group(new[] { new DamageSample(key, round * 100, 1, 40, 5, 20,
                Squads: new[] { new SquadStats("a", "Melter Gary", round * 100, 1, 40, 20, 5, 0.125 * round, 0.375 * round) }) });
            var right = DamageModel.Group(new[] { new DamageSample(key, 80, 2, 120,
                Squads: new[] { new SquadStats("b", "Melter Bob", 80, 2, 120, XpEarned: 0.75 * round, EnemyXpAwarded: 0.25 * round) }) });
            h.Put(round, left, right, true);
        }
        return h;
    }
    private static void Scopes()
    {
        foreach (var scope in Enum.GetValues<StatsScope>())
        {
            Check(StatsPresentation.Damage(scope, true) == (scope is StatsScope.All or StatsScope.Damage), "Wrong damage scope");
            Check(StatsPresentation.Tanking(scope, true) == (scope is StatsScope.All or StatsScope.Tanking), "Wrong tanking scope");
            Check(StatsPresentation.Experience(scope, true) == (scope is StatsScope.All or StatsScope.Experience), "Wrong XP scope");
            Check(StatsPresentation.Kills(scope, true) == (scope is StatsScope.All or StatsScope.Kills), "Wrong kills scope");
            Check(!StatsPresentation.Damage(scope, false) && !StatsPresentation.Tanking(scope, false)
                && !StatsPresentation.Experience(scope, false) && !StatsPresentation.Kills(scope, false), "Hidden parameter reappeared");
        }
        Check(StatsPresentation.Metric(StatsScope.Experience) == ResultMetric.XpEarned, "XP tab selected a damage graph");
    }
    private static void Experience()
    {
        var h = History();
        var earned = h.MetricSeries(ResultMetric.XpEarned, false);
        var awarded = h.MetricSeries(ResultMetric.EnemyXpAwarded, false);
        Check(earned[0].Left == 0.125 && earned[1].Left == 0.25 && earned[1].Right == 1.5, "XP graph truncated or crossed teams");
        Check(awarded[0].Left == 0.375 && awarded[1].Right == 0.5, "XP graph selected the wrong channel");
        Check(h.MetricSeries(ResultMetric.XpEarned, true)[1].Left == 0.375, "Cumulative XP counted a round twice");
        h.Resume(1); Check(h.MetricSeries(ResultMetric.XpEarned, true).Single().Left == 0.125, "Rewind graph retained future XP");
    }
    private static void Overlay()
    {
        var h = History();
        Check(StatsPresentation.CompletedBattle(h, true) is null, "Live polling was replaced by a completed snapshot");
        h.End(true);
        var snapshot = StatsPresentation.CompletedBattle(h, true)!;
        Check(snapshot.Left.TotalDealt == 220 && snapshot.Left.Rows.Single().XpEarned == 0.25, "F8 fallback used overall or stale data");
        Check(snapshot.Available && snapshot.LeftName == "Left", "Overlay fallback lost sides");
        Check(StatsPresentation.CompletedBattle(h, false) is null, "Old overlay leaked into the lobby");
        h.Resume(0); Check(StatsPresentation.CompletedBattle(h, true) is null, "Retry retained the finished overlay");
    }
    private static void Immutable()
    {
        var h = History(); var before = h.Overall().Left;
        foreach (var scope in Enum.GetValues<StatsScope>()) h.MetricSeries(StatsPresentation.Metric(scope), true);
        var after = h.Overall().Left;
        Check(after.TotalDealt == before.TotalDealt && after.TotalDamageTaken == before.TotalDamageTaken
            && after.Rows.Single().XpEarned == before.Rows.Single().XpEarned, "Display scopes mutated match data");
        Check(h.MetricSeries(ResultMetric.Damage, false)[0].Left == h.Series(ResultMetric.Damage, false)[0].Left, "Graph lost combined overkill total");
    }
    private static void Bars()
    {
        Check(DamageModel.BarFraction(0.125, 0.5) == 0.25f, "Fractional XP bar truncated");
        Check(DamageModel.BarFraction(double.NaN, 1d) == 0 && DamageModel.BarFraction(1d, 0d) == 0
            && DamageModel.BarFraction(2d, 1d) == 1, "Invalid graph bounds");
    }
    private static void Windows()
    {
        Check(!OverlayWindowPolicy.Blocks(false, false, false, true, true, true, true), "System overlay notification hid panels");
        Check(!OverlayWindowPolicy.Blocks(false, false, true, false, true, true, true), "Background-free HUD hid panels");
        Check(!OverlayWindowPolicy.Blocks(true, false, true, true, true, false, true), "Hidden canvas hid panels");
        Check(!OverlayWindowPolicy.Blocks(true, false, true, true, false, true, true), "Inactive menu hid panels");
        Check(OverlayWindowPolicy.Blocks(true, false, false, false, true, true, true), "Visible settings/menu did not suppress panels");
        Check(OverlayWindowPolicy.Blocks(false, false, true, true, true, true, true), "Modal window did not suppress panels");
        Check(!OverlayWindowPolicy.Blocks(false, true, true, true, true, true, false), "Collapsed reinforcement UI hid panels");
        Check(OverlayWindowPolicy.Blocks(false, true, true, false, true, true, true), "Open reinforcement choice did not suppress panels");
        Check(OverlayWindowPolicy.Blocks(false, false, false, false, true, true, true, tooltip: true), "Visible background-free tooltip did not suppress panels");
        Check(!OverlayWindowPolicy.Blocks(false, false, false, false, true, true, false, tooltip: true), "Pooled tooltip with hidden content hid panels");
        Check(!OverlayWindowPolicy.Blocks(false, false, false, false, true, false, true, tooltip: true), "Hidden tooltip canvas hid panels");
        Check(!OverlayWindowPolicy.Blocks(false, false, false, false, false, true, true, tooltip: true), "Inactive tooltip window hid panels");
    }
    private static void HudPlacement()
    {
        var controls = new[] { new HudBounds(8, 205, 100, 275), new HudBounds(8, 280, 100, 345),
            new HudBounds(8, 350, 100, 415), new HudBounds(1650, 630, 1912, 1060) };
        var layout = SideHudLayout.Arrange(controls, 1920, 220, 900, false, 246);
        Check(layout.Left.Inset == 108 && layout.Left.Bottom == 900, "Controls outside the shifted panel reduced it to one row");
        Check(layout.Right.Inset == 8 && layout.Right.Bottom == 622, "Right shop no longer limits the panel height");
        Check(SideHudLayout.Arrange(controls.Reverse(), 1920, 220, 900, false, 246) == layout,
            "Native component order changed pagination");
        var withShop = SideHudLayout.Arrange(controls.Append(new HudBounds(8, 700, 470, 1060)), 1920, 220, 900, false, 246);
        Check(withShop.Left.Bottom == 692, "Shifted panel overlaps the selected-unit/shop HUD");
        var spectator = SideHudLayout.Arrange(controls, 1920, 220, 940, true, 196);
        Check(spectator.Left.Inset == 108 && spectator.Right.Inset == 278
            && spectator.Left.Bottom == 940 && spectator.Right.Bottom == 940, "Spectator lane lost full available height");
        foreach (var watching in new[] { false, true })
        {
            var menu = new[] { new HudBounds(8, 750, 480, 1080) };
            var withUnit = SideHudLayout.Arrange(controls, 1920, 220, 940, watching, 196, menu);
            Check(withUnit.Left.Bottom == 742, "Selected-unit menu does not reserve height");
            var withoutUnit = SideHudLayout.Arrange(controls, 1920, 220, 940, watching, 196);
            Check(withUnit.Right == withoutUnit.Right, "Left unit menu changed the opposite overlay");
            Check(withUnit.Left.Inset == withoutUnit.Left.Inset, "Unit menu pushed the overlay into the battlefield");
        }
        var raisedMenu = SideHudLayout.Arrange(Array.Empty<HudBounds>(), 1920, 220, 940, true, 196,
            new[] { new HudBounds(8, 270, 480, 1080) });
        Check(raisedMenu.Left.Bottom == 262, "Minimum height forced overlay through a raised unit menu");
        var clear = SideHudLayout.Arrange(new[] { new HudBounds(0, 220, 1920, 1080),
            new HudBounds(0, 0, 100, 200), new HudBounds(0, 950, 100, 1080) }, 1920, 220, 900, false, 246);
        Check(clear.Left == new HudLane(8, 900) && clear.Right == new HudLane(8, 900), "Offscreen/decoration bounds reduce pagination");
    }
}
