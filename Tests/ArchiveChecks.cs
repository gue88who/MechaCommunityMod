using MechaCommunityMod.Plugins.BattleStatistics;

internal static class ArchiveChecks
{
    internal static readonly (string, Action)[] All = {
        ("Map round and close clicks use stable actions and ignore disabled rounds and canceled presses", () => {
            var input = new BoardMapInput();
            var buttons = new[] { new BoardMapButton(-1, 600.25, 100.5, 24, 24),
                new BoardMapButton(0, 500.25, 140.5, 40, 26), new BoardMapButton(1, 544.25, 140.5, 40, 26, false),
                new BoardMapButton(2, 588.25, 140.5, 40, 26) };
            input.Press(buttons, 510, 150); Assert(input.Captured, "All round did not capture its press");
            // Rendering order and table GUI control numbers must not affect the action.
            Assert(input.Release(buttons.Reverse(), 510, 150) == 0, "All round click lost across render order changes");
            input.Press(buttons, 598, 150); Assert(input.Release(buttons, 598, 150) == 2, "Round selection failed");
            input.Press(buttons, 550, 150); Assert(!input.Captured && input.Release(buttons, 550, 150) is null, "Disabled round accepted a click");
            input.Press(buttons, 510, 150); Assert(input.Release(buttons, 598, 150) is null, "Release on a different button activated it");
            input.Press(buttons, 510, 150); Assert(input.Release(buttons, 400, 80) is null, "Release outside the popup activated a round");
            Assert(input.Release(buttons, 598, 150) is null, "Release without a press activated a round");
            input.Press(buttons, 610, 110); Assert(input.Release(buttons, 610, 110) == -1, "Close click was blocked");
            input.Press(buttons, 510, 150); input.Cancel();
            Assert(!input.Captured && input.Release(buttons, 510, 150) is null, "Hidden popup retained a pressed button");
            input.Press(buttons, 540.25, 150); Assert(!input.Captured, "Fractional right edge captured a button outside its bounds");
            input.Press(buttons, 598, 150);
            Assert(input.Release(buttons.Select(b => b with { Enabled = false }), 598, 150) is null, "Round disabled after press still activated");
        }),
        ("Player HP damage survives repairs negative ending HP and separate teams", () => {
            var changes = new PlayerHpChanges();
            Assert(changes.Loss(0) is null, "Missing HP observation became a zero");
            changes.Observe(0, 100, 40); changes.Observe(0, 40, 80); changes.Observe(0, 80, -20);
            changes.Observe(1, 200, 200);
            Assert(changes.Loss(0) == 160 && changes.After(0) == -20, "HP repairs masked damage or negative ending HP was clamped");
            Assert(changes.Loss(1) == 0 && changes.After(1) == 200, "Teams shared HP loss");
            changes.Clear(); Assert(changes.Loss(0) is null, "HP loss leaked across rounds");
        }),
        ("Round winners HP loss and squad core contributions survive saves refresh and rewind", RoundResults),
        ("Core contribution reconciliation refuses unobserved HP damage and invalid scores", CoreContributions),
        ("Stats file round trip retains all report channels", RoundTrip),
        ("Stats embeds avatars after asynchronous download without extra files", Avatars),
        ("Board snapshots preserve flanks towers round selection and captured squad identity", BoardMaps),
        ("Squad outlines exclude internal cells and preserve touching unit boundaries", BoardBorders),
        ("Stats checkpoints replace one file and preserve other sessions", Checkpoints),
        ("Stats checkpoints follow rewind, finish and departure", Lifecycle),
        ("Stats writer reports failure without breaking recording", Failure),
        ("Stats format rejects unknown schema versions", Version)
    };

    private static void CoreContributions()
    {
        var split = CoreDamageAttribution.Attribute(new[] { ("squad", 20L), ("squad", 30L), ("spawn", 5L) }, 55)!;
        Assert(split["squad"] == 50 && split["spawn"] == 5 && split.Values.Sum() == 55, "Survivor scores duplicated or lost");
        Assert(CoreDamageAttribution.Attribute(new[] { ("unit", 50L) }, 60) is null, "Unobserved modifier was silently allocated");
        Assert(CoreDamageAttribution.Attribute(new[] { ("unit", -5L), ("other", 10L) }, 5) is null, "Negative score accepted");
        Assert(CoreDamageAttribution.Attribute(Array.Empty<(string, long)>(), 20) is null, "Missing observation invented core damage");
        Assert(CoreDamageAttribution.Attribute(Array.Empty<(string, long)>(), 0)!.Count == 0, "Recorded zero became unavailable");
    }
    private static void RoundResults()
    {
        var (history, costs) = Fixture();
        var unit = new UnitKey(0, 5); var empty = DamageModel.Group(Array.Empty<DamageSample>());
        var squad = new SquadStats("one", "One", CoreDamage: 35);
        var team = DamageModel.Group(new[] { new DamageSample(unit, 0, 0, Squads: new[] { squad }) });
        history.Put(1, team, empty, true);
        var info = new RoundInfo(RoundWinner.Blue, 0, 35, 100, 100, 20, -15, 0, 0);
        history.SetRoundInfo(1, info);
        history.Put(1, team, empty, true);
        Assert(history.Find(1)!.Info == info, "Polling erased the round outcome");
        history.Put(2, team, empty, true);
        history.SetRoundInfo(2, info with { Winner = RoundWinner.Draw, RightHpDamage = 0 });
        var saved = StatsFormat.Deserialize(StatsFormat.Serialize(Capture(history, costs)));
        Assert(saved.Rounds[0].Info == info && saved.Rounds[1].Info!.Winner == RoundWinner.Draw, "Winner or negative ending HP lost");
        Assert(saved.Rounds[0].Left.Units.Single().CoreDamage == 35
            && saved.Rounds[0].Left.Units.Single().Squads!.Single().CoreDamage == 35
            && saved.Overall.Left.Units.Single().CoreDamage == 70, "Core damage did not survive aggregation/export");
        history.Resume(1);
        Assert(history.Find(1)!.Info == info && history.Find(2) is null, "Rewind retained the future outcome");
        history.Put(2, DamageModel.Group(new[] { new DamageSample(unit, 0, 0,
            Squads: new[] { squad with { CoreDamage = null } }) }), empty, true);
        Assert(history.Overall().Left.Rows.Single().CoreDamage is null, "Unknown round was silently counted as zero");
        var old = System.Text.Json.Nodes.JsonNode.Parse(StatsFormat.Serialize(Capture(history, costs)))!.AsObject();
        foreach (var round in old["rounds"]!.AsArray())
        {
            round!.AsObject().Remove("info");
            foreach (var row in round["left"]!["units"]!.AsArray())
            {
                row!.AsObject().Remove("coreDamage");
                foreach (var s in row["squads"]!.AsArray()) s!.AsObject().Remove("coreDamage");
            }
        }
        var compatible = StatsFormat.Deserialize(old.ToJsonString());
        Assert(compatible.Rounds[0].Info is null && compatible.Rounds[0].Left.Units.Single().CoreDamage is null
            && compatible.Rounds[0].Left.Units.Single().Squads!.Single().CoreDamage is null, "Old reports no longer readable");
    }

    private static (MatchHistory History, UnitCostHistory Costs) Fixture()
    {
        var history = new MatchHistory();
        var costs = new UnitCostHistory();
        var identity = new MatchIdentity(12345, 67890, 3);
        history.Begin(identity, "玩家 / test", "Opponent");
        costs.Begin(identity);
        var unit = new UnitKey(0, 5);
        var tech = new DamageSource(DamageCategory.Tech, 7, "Secondary Armament");
        var team = DamageModel.Group(new[] {
            new DamageSample(unit, 120, 2, 85, 5, 20,
                new[] { new SourceDamage(tech, 120, 20) },
                new[] { new SourceDamage(DamageSource.Attack, 80, 5) },
                new HackStats(50, 25, 10, 1),
                new[] { new Contribution(UnitOrigin.Spawned, unit, new UnitKey(0, 9), false,
                    120, 80, 20, 5, 2, "Production") }),
            new DamageSample(new UnitKey(102, 77), 30, 1,
                DealtSources: new[] { new SourceDamage(new(DamageCategory.Spell, 77, "Missile"), 30, 0) }, Casts: 2)
        });
        history.Put(1, team, DamageModel.Group(Array.Empty<DamageSample>()), true);
        costs.Put(1, new() { [unit] = new(200, 100, 300) }, new());
        return (history, costs);
    }
    private static StatsDocument Capture(MatchHistory h, UnitCostHistory c) =>
        StatsFormat.Capture(h, c, key => "Unit " + key.Id, "test");
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void BoardBorders()
    {
        var piece = new BoardPiece("a", new(0, 1), false, false,
            new[] { new BoardRect(0, 0, 1, 2), new BoardRect(1, 0, 1, 2) });
        var edges = BoardOutline.Edges(piece);
        Assert(edges.Length == 8, "Internal cell seams were outlined");
        Assert(!edges.Contains(new BoardEdge(0, 0, 1)) && !edges.Contains(new BoardEdge(1, 0, 0)), "Adjacent cells within squad separated");
        var next = piece with { Id = "b", Cells = new[] { new BoardRect(2, 0, 1, 2) } };
        Assert(edges.Contains(new BoardEdge(1, 0, 1)) && BoardOutline.Edges(next).Contains(new BoardEdge(2, 0, 0)), "Touching squads lost their boundary");
        Assert(BoardOutline.Edges(piece with { Cells = new[] { new BoardRect(0, 0, 1, 1) } }).Length == 4, "Single-cell outline missing");
    }
    private static void BoardMaps()
    {
        var unit = new UnitKey(0, 5);
        var board = new BoardSnapshot(1, new[] {
            new BoardRect(0, 0, 20, 20), new BoardRect(-5, 3, 5, 6), new BoardRect(20, 12, 5, 6)
        }, new[] {
            new BoardPiece("deployed:1:1", unit, false, false, new[] { new BoardRect(0, 2, 2, 3) }),
            new BoardPiece("deployed:2:2", unit, true, false, new[] { new BoardRect(20, 14, 2, 3) }),
            new BoardPiece("tower:1:0", new(-1, 0), false, true, new[] { new BoardRect(4, 4, 2, 2) })
        });
        Assert(board.Bounds() == new BoardRect(-5, 0, 30, 20), "Flanks omitted from bounds");
        Assert(!board.FlipVertically(), "Blue-bottom formation was reversed");
        var reversed = board with { Pieces = board.Pieces.Select(p => p with { Right = !p.Right }).ToArray() };
        Assert(reversed.FlipVertically(), "Blue-top formation was not reversed");
        var flank = new BoardRect(20, 14, 2, 3);
        Assert(BoardSnapshot.DisplayRect(flank, board.Bounds(), false) == new BoardRect(25, 3, 2, 3),
            "Default orientation moved the flank");
        Assert(BoardSnapshot.DisplayRect(flank, board.Bounds(), true) == new BoardRect(3, 14, 2, 3),
            "Rotated board mirrored the flank instead of rotating both axes");
        var row = new StatsTableRow("deployed:1:1", unit, false, "Gary", 0, 0, 0, 0, 0, 0, 0, 0, 0);
        Assert(board.Select(row).Single().Id == row.Id, "Named squad highlighted wrong side or type");
        Assert(board.Select(row with { Id = "type:" + unit }).Single().Right == false, "Grouped selection crossed teams");
        var captured = row with { Id = "child:hacker:5", MapIds = new[] { "deployed:2:2" } };
        Assert(board.Select(captured).Single().Right, "Captured squad lost original position");
        var history = new BoardHistory(); history.Put(board);
        history.Put(board with { Round = 2, Pieces = Array.Empty<BoardPiece>() });
        Assert(history.Get(0)!.Round == 2 && history.Get(1)!.Pieces.Length == 3, "Round selection lost historical formation");
        Assert(history.Find(row, 0)!.Round == 1 && history.Find(row, 2)!.Round == 1,
            "Sold unit did not resolve its latest available round");
        history.Resume(1);
        Assert(history.Get(2) is null && history.Get(0)!.Round == 1, "Rewind retained future formation");
        var (match, costs) = Fixture();
        var saved = StatsFormat.Deserialize(StatsFormat.Serialize(Capture(match, costs) with { Boards = history.All() }));
        Assert(saved.Boards!.Single().Pieces.Single(p => p.Tower).Cells.Single() == new BoardRect(4, 4, 2, 2), "Tower cells lost in single report");
        Assert(saved.Boards!.Single().Regions.Any(r => r.X < 0), "Saved report omitted flank region");
        history.Clear(); Assert(history.Get(0) is null, "Formation leaked across matches");
    }
    private static void Avatars()
    {
        var (history, costs) = Fixture();
        var directory = Path.Combine(Path.GetTempPath(), "mech-avatar-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var archive = new StatsArchive(directory, "test");
            var pending = new TaskCompletionSource<SavedAvatar?>();
            archive.Observe(history, costs, _ => "Unit", leftAvatar: pending.Task);
            Assert(!archive.PendingWrites.IsCompleted, "Writer did not wait for avatar");
            var bytes = new byte[] { 1, 2, 3, 4 };
            var avatar = new SavedAvatar("https://avatars.steamstatic.com/test.jpg", "image/jpeg", Convert.ToBase64String(bytes));
            pending.SetResult(avatar);
            archive.PendingWrites.GetAwaiter().GetResult();
            var files = Directory.GetFiles(directory);
            Assert(files.Length == 1 && files[0].EndsWith(".mechstats"), "Avatar created a separate file");
            var saved = StatsFormat.Deserialize(File.ReadAllText(files[0]));
            Assert(saved.LeftAvatar == avatar && saved.RightAvatar is null, "Embedded avatar lost");
            Assert(Convert.FromBase64String(saved.LeftAvatar!.Base64).SequenceEqual(bytes), "Avatar bytes changed");
            var old = System.Text.Json.Nodes.JsonNode.Parse(StatsFormat.Serialize(saved))!.AsObject();
            old.Remove("leftAvatar"); old.Remove("rightAvatar");
            Assert(StatsFormat.Deserialize(old.ToJsonString()).LeftAvatar is null, "Old reports no longer supported");
            var downloads = new PlayerAvatars();
            Assert(downloads.Get("file:///test.jpg").Result is null, "Local paths accepted as downloads");
            Assert(downloads.Get("https://example.com/test.jpg").Result is null, "Non-avatar host accepted");
            var nativeAvatar = avatar with { Source = "selected-game-avatar", ContentType = "image/png" };
            downloads.Put(nativeAvatar);
            Assert(downloads.Get(nativeAvatar.Source).Result == nativeAvatar && downloads.Ready(nativeAvatar.Source) == nativeAvatar,
                "Selected game avatar was rejected or replaced by the Steam download");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static void RoundTrip()
    {
        var (history, costs) = Fixture();
        history.End(true, "玩家 / test won");
        var before = Capture(history, costs);
        var json = StatsFormat.Serialize(before);
        var after = StatsFormat.Deserialize(json);
        Assert(StatsFormat.Serialize(after) == json, "Round trip lost fields");
        var row = after.Rounds[0].Left.Units.Single(r => r.Unit.Id == 5);
        Assert(row.Damage == 120 && row.Overkill == 20 && row.Taken == 80 && row.TakenOverkill == 5, "Damage semantics changed");
        Assert(row.Hacking == new HackStats(50, 25, 10, 1), "Hacking fields lost");
        Assert(row.DealtSources[0].Source.Id == 7 && row.DealtSources[0].Overkill == 20, "Tech attribution lost");
        Assert(row.Contributions[0].Origin == UnitOrigin.Spawned && row.Contributions[0].ParentName == "Unit 9"
            && row.Contributions[0].Taken == 80, "Spawn attribution lost");
        Assert(after.Overall.Left.Units.Single(r => r.Unit.Kind == 102).Casts == 2, "Spell uses lost");
        Assert(after.Overall.Left.Costs[0].Base == 200 && after.Overall.Left.Costs[0].Technology == 300, "Cost lost");
        Assert(after.LeftName == history.LeftName && after.Finished && after.Outcome == history.Outcome, "Metadata lost");
        Assert(!json.Contains("12345") && !json.Contains("67890"), "Native pointers exported");
    }
    private static void InDirectory(Action<string> run)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mechstats-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { run(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
    private static void Checkpoints() => InDirectory(directory =>
    {
        var (history, costs) = Fixture();
        var first = Capture(history, costs);
        var path = StatsFormat.Write(directory, first);
        history.End(true);
        var second = Capture(history, costs);
        Assert(StatsFormat.Write(directory, second) == path, "Checkpoint created a second file");
        Assert(StatsFormat.Deserialize(File.ReadAllText(path)).Finished, "Final result not saved");
        var (other, otherCosts) = Fixture();
        StatsFormat.Write(directory, Capture(other, otherCosts));
        Assert(Directory.GetFiles(directory).Length == 2, "Sessions collided or temporary files left behind");
    });
    private static void Lifecycle() => InDirectory(directory =>
    {
        var (history, costs) = Fixture();
        var archive = new StatsArchive(directory, "test");
        archive.Observe(history, costs, key => "Unit " + key.Id);
        archive.PendingWrites.GetAwaiter().GetResult();
        var firstTask = archive.PendingWrites;
        history.Put(1, history.Find(1)!.Left, history.Find(1)!.Right, true);
        archive.Observe(history, costs, key => "Unit " + key.Id);
        Assert(ReferenceEquals(firstTask, archive.PendingWrites), "Polling queues repeated disk writes");
        history.Put(2, history.Find(1)!.Left, history.Find(1)!.Right, true);
        archive.Observe(history, costs, key => "Unit " + key.Id);
        history.Resume(1);
        archive.Observe(history, costs, key => "Unit " + key.Id);
        history.End(false);
        archive.Observe(history, costs, key => "Unit " + key.Id);
        archive.PendingWrites.GetAwaiter().GetResult();
        var saved = StatsFormat.Deserialize(File.ReadAllText(Directory.GetFiles(directory).Single()));
        Assert(saved.Rounds.Length == 1 && saved.Ended && !saved.Finished, "Rewind/exit kept stale future rounds");
        history.End(true, "Winner");
        archive.Observe(history, costs, key => "Unit " + key.Id);
        archive.PendingWrites.GetAwaiter().GetResult();
        saved = StatsFormat.Deserialize(File.ReadAllText(Directory.GetFiles(directory).Single()));
        Assert(saved.Finished && saved.Outcome == "Winner", "Late outcome was not updated");
        history.Resume(0);
        costs.Resume(0);
        archive.Observe(history, costs, key => "Unit " + key.Id);
        archive.PendingWrites.GetAwaiter().GetResult();
        saved = StatsFormat.Deserialize(File.ReadAllText(Directory.GetFiles(directory).Single()));
        Assert(saved.Rounds.Length == 0 && !saved.Ended && !saved.Finished, "Rewind to start left old results on disk");
    });
    private static void Failure() => InDirectory(directory =>
    {
        var blocked = Path.Combine(directory, "blocked");
        File.WriteAllText(blocked, "file, not directory");
        var (history, costs) = Fixture();
        var archive = new StatsArchive(blocked, "test");
        archive.Observe(history, costs, key => "Unit " + key.Id);
        archive.PendingWrites.GetAwaiter().GetResult();
        var errors = 0;
        archive.DrainMessages((error, _) => { if (error) errors++; });
        Assert(errors == 1 && history.Count == 1, "I/O error not isolated");
    });
    private static void Version()
    {
        var (h, c) = Fixture();
        try { StatsFormat.Deserialize(StatsFormat.Serialize(Capture(h, c) with { Version = 999 })); }
        catch (InvalidDataException) { return; }
        throw new Exception("Unknown version accepted");
    }
}
