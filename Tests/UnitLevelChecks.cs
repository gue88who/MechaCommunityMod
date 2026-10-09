using MechaCommunityMod.Plugins.BattleStatistics;

internal static class UnitLevelChecks
{
    internal static readonly (string, Action)[] All = {
        ("Level snapshots replace polling samples, preserve XP carry and rewind independently by team", Recording),
        ("All-round levels use the latest round rather than summing levels or choosing the largest XP", History),
        ("Grouped level badges distinguish mixed, unknown, capped and matching levels", Badges),
        ("Parent totals and source breakdowns do not borrow attributed units' levels", Children),
        ("Saved levels and XP thresholds round trip and remain optional in older files", Archive),
        ("XP ring mask progresses clockwise with a continuous fill and antialiased edge", Arc)
    };
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static SquadStats Squad(string id, params UnitLevelSnapshot[] levels) => new(id, id, Levels: levels);
    private static readonly UnitKey Unit = new(0, 1);
    private static TeamStats Team(params SquadStats[] squads) => DamageModel.Group(new[] {
        new DamageSample(Unit, squads.Sum(s => s.Damage), squads.Sum(s => s.Kills), Squads: squads) });
    private static readonly TeamStats Empty = DamageModel.Group(Array.Empty<DamageSample>());
    private static MatchHistory NewHistory()
    {
        var h = new MatchHistory(); h.Begin(new(1, 2, 3), "Blue", "Red"); return h;
    }
    private static void Recording()
    {
        var ledger = new UnitLevelLedger();
        var first = new UnitLevelSnapshot(1, 1, 75.5, 100, false);
        ledger.Put(10, 0, "a", first); ledger.Put(10, 0, "a", first with { Experience = 100 });
        ledger.Put(20, 0, "a", new UnitLevelSnapshot(2, 2, 15, 200, false));
        ledger.Put(10, 1, "a", new UnitLevelSnapshot(1, 4, 20, 400, false));
        Check(ledger.Get(10, 0, "a").Single().Progress == 1 && ledger.Get(10, 1, "a").Single().Level == 4,
            "Repeated polling accumulated XP or crossed armies");
        ledger.Retain(new[] { 10L });
        Check(ledger.Get(20, 0, "a").Length == 0 && ledger.Get(10, 0, "a").Length == 1, "Rewind kept future levels");
        ledger.Clear(); Check(ledger.Get(10, 0, "a").Length == 0, "New match reused old levels");
        Check(first.Progress == .755, "Fractional stored XP was rounded");
    }
    private static void History()
    {
        var h = NewHistory();
        var snapshots = new[] { new UnitLevelSnapshot(1, 1, 100, 100, false) };
        h.Put(1, Team(Squad("a", snapshots)), Empty, true);
        snapshots[0] = new(1, 9, 0, 0, true);
        h.Put(2, Team(Squad("a", new UnitLevelSnapshot(2, 2, 10.5, 200, false))), Empty, true);
        var before = h.Find(1)!.Left.Rows.Single().Squads!.Single();
        Check(before.Levels!.Single().Level == 1, "Mutable snapshot array corrupted a previous round");
        var all = h.Overall().Left.Rows.Single().Squads!.Single();
        var badge = UnitLevelBadge.From(new[] { all })!;
        Check(all.Levels!.Length == 2 && badge.Label == "2" && badge.Progress == .0525
            && badge.Hint.Contains("R1:") && badge.Hint.Contains("R2:"), "All summed levels, dropped history or reused old XP");
        h.Resume(1);
        Check(UnitLevelBadge.From(h.Overall().Left.Rows.Single().Squads!)!.Label == "1", "Rewind kept a future level");
    }
    private static void Badges()
    {
        var a = Squad("a", new UnitLevelSnapshot(1, 2, 20, 100, false));
        var b = Squad("b", new UnitLevelSnapshot(1, 2, 80, 100, false));
        Check(UnitLevelBadge.From(new[] { a, b }) is { Label: "2", Progress: .5 }, "Matching levels lost combined progress");
        Check(UnitLevelBadge.From(new[] { a, b with { Levels = new[] { new UnitLevelSnapshot(1, 4, 10, 200, false) } } })
            is { Label: "2–4", Progress: null }, "Mixed levels used a misleading average");
        Check(UnitLevelBadge.From(new[] { a, new SquadStats("old", "old") }) is { Label: "?", Progress: null }, "Missing observations became level zero");
        Check(UnitLevelBadge.From(new[] { new SquadStats("old", "old") }) is null, "Old file invented a badge");
        Check(UnitLevelBadge.From(new[] { Squad("max", new UnitLevelSnapshot(3, 9, 50, 100, true)) }) is { Label: "9", Progress: null }, "Max level advertised another level");
        Check(new UnitLevelSnapshot(1, 1, 150, 100, false).Progress == 1
            && new UnitLevelSnapshot(1, 1, -20, 100, false).Progress == 0
            && new UnitLevelSnapshot(1, 1, double.NaN, 100, false).Progress is null
            && new UnitLevelSnapshot(1, 1, 20, 0, false).Progress is null, "Invalid XP produced an invalid circle");
        Check(UnitLevelBadge.From(new[] { a, a })!.Progress == .2, "Duplicate squad snapshots changed progress");
    }
    private static void Children()
    {
        var parent = Squad("parent", new UnitLevelSnapshot(1, 3, 20, 100, false)) with { Damage = 10 };
        var child = Squad("child", new UnitLevelSnapshot(1, 1, 50, 100, false)) with { ParentId = "parent", ParentUnit = Unit, Spawned = true, Damage = 20 };
        var rows = StatsTable.Sort(StatsTable.Rows(Team(parent, child), false, true, null, _ => "Unit"), StatsColumn.Damage, true);
        Check(rows.Single(r => r.Id == "parent").Damage == 30
            && UnitLevelBadge.From(rows.Single(r => r.Id == "parent").LevelSquads)!.Label == "3"
            && UnitLevelBadge.From(rows.Single(r => r.Spawned).LevelSquads)!.Label == "1", "Parent inherited a spawned unit's level");
        var details = StatsTable.Details(rows, rows.Select(r => (r.Right, r.Id)).ToHashSet());
        Check(details.Where(r => r.Breakdown).All(r => r.LevelSquads.Length == 0), "Damage sources were assigned unit levels");
        var grouped = StatsTable.Rows(Team(parent, child), false, false, null, _ => "Unit");
        Check(UnitLevelBadge.From(grouped.Single(r => r.Id.StartsWith("type:")).LevelSquads)!.Label == "3", "Grouped parent mixed child levels into its badge");
    }
    private static void Archive()
    {
        var h = NewHistory(); h.Put(1, Team(Squad("a", new UnitLevelSnapshot(1, 2, 12.25, 200, false))), Empty, true);
        var document = StatsFormat.Capture(h, new(), _ => "Unit", "test");
        var json = StatsFormat.Serialize(document); var loaded = StatsFormat.Deserialize(json);
        var value = loaded.Rounds.Single().Left.Units.Single().Squads!.Single().Levels!.Single();
        Check(value.Level == 2 && value.Experience == 12.25 && value.NextLevelExperience == 200, "Export lost XP threshold or level");
        var old = document with { Rounds = document.Rounds.Select(r => r with { Left = r.Left with {
            Units = r.Left.Units.Select(u => u with { Squads = u.Squads!.Select(s => s with { Levels = null }).ToArray() }).ToArray() } }).ToArray() };
        var node = System.Text.Json.Nodes.JsonNode.Parse(StatsFormat.Serialize(old))!;
        var squad = node["rounds"]![0]!["left"]!["units"]![0]!["squads"]![0]!.AsObject(); squad.Remove("levels");
        Check(StatsFormat.Deserialize(node.ToJsonString()).Rounds.Single().Left.Units.Single().Squads!.Single().Levels is null,
            "An older report without levels failed to load");
    }
    private static void Arc()
    {
        Check(UnitLevelArc.Key(-1) == 0 && UnitLevelArc.Key(2) == 360 && UnitLevelArc.Key(double.NaN) == 0,
            "Invalid progress produced an invalid mask key");
        Check(UnitLevelArc.Coverage(0, .5, .05, 1d / 64, 1d / 64) == 0
            && UnitLevelArc.Coverage(1, .5, .05, 1d / 64, 1d / 64) == 1
            && UnitLevelArc.Coverage(double.NaN, .5, .05, 1d / 64, 1d / 64) == 0, "Empty or full ring is wrong");
        foreach (var size in new[] { 32, 64, 128 })
        foreach (var progress in new[] { .02, .07, .15, .25, .375, .5, .625, .75, .875, 1 })
        {
            for (var degree = 1; degree < 360; degree++)
            {
                // Stay outside the antialiased start and moving edges.
                if (degree < 3 || degree > 357 || Math.Abs(degree - progress * 360) < 3) continue;
                var angle = degree * Math.PI / 180;
                var coverage = UnitLevelArc.Coverage(progress, .5 + .45 * Math.Sin(angle), .5 - .45 * Math.Cos(angle), 1d / size, 1d / size);
                Check(coverage == (degree < progress * 360 ? 1 : 0),
                    $"Gap or wrong clockwise fill at {progress:P0}, {degree} degrees, {size}px");
            }
        }
        Check(UnitLevelArc.Coverage(.25, .95, .5, 1d / 64, 1d / 64) == .5,
            "Moving edge lost antialiasing");
        for (var degree = 0; degree < 360; degree += 5)
        {
            var angle = degree * Math.PI / 180; var previous = 0d;
            for (var step = 0; step <= 100; step++)
            {
                var coverage = UnitLevelArc.Coverage(step / 100d, .5 + .45 * Math.Sin(angle), .5 - .45 * Math.Cos(angle), 1d / 64, 1d / 64);
                Check(coverage >= previous && coverage <= 1, "Increasing XP removed part of the fill");
                previous = coverage;
            }
        }
    }
}
