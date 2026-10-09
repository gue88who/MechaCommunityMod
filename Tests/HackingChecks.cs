using MechaCommunityMod.Plugins.BattleStatistics;
using System.Text.Json.Nodes;

internal static class HackingChecks
{
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }

    internal static readonly (string, Action)[] All = {
        ("Hacking retains each squad's successful failed and pending progress and counts cooperative conversions once", () => {
            var ledger = new HackLedger(); var unit = new UnitKey(0, 14);
            ledger.Add(1, 10, 0, unit, 80, "gary");
            ledger.Add(2, 10, 0, unit, 120, "bob");
            ledger.Add(3, 10, 1, unit, 15, "enemy");
            Equal(new HackStats(Pending: 80), ledger.Squads(0)["gary"]);
            // The final pulse is recorded before native conversion cleanup.
            ledger.Add(1, 10, 0, unit, 20, "gary");
            ledger.BeginConversion(10); ledger.End(1, 10); ledger.End(2, 10);
            ledger.Complete(10, 0); ledger.EndConversion(10);
            Equal(new HackStats(100, 0, 0, 1), ledger.Squads(0)["gary"]);
            Equal(new HackStats(120), ledger.Squads(0)["bob"]);
            Equal(new HackStats(0, 15), ledger.Squads(1)["enemy"]);
            ledger.Add(1, 11, 0, unit, 25, "gary"); ledger.End(1, 11);
            ledger.Add(1, 11, 0, unit, 40, "gary"); ledger.Complete(11, 0);
            ledger.Add(2, 12, 0, unit, 30, "bob"); ledger.Died(12);
            ledger.Add(2, 13, 0, unit, 50, "bob"); ledger.Died(2);
            ledger.Add(1, 14, 0, unit, 10, "gary"); ledger.Finish();
            ledger.Died(1); ledger.Died(10); ledger.Finish();
            Equal(new HackStats(140, 35, 0, 2), ledger.Squads(0)["gary"]);
            Equal(new HackStats(120, 80), ledger.Squads(0)["bob"]);
            Equal(ledger.Snapshot(0)[unit], HackStats.Sum(ledger.Squads(0).Values));
        }),
        ("Reused hacking actor pointers cannot merge different squads or armies", () => {
            var ledger = new HackLedger(); var unit = new UnitKey(0, 14);
            ledger.Add(1, 10, 0, unit, 20, "gary");
            ledger.Add(1, 10, 0, unit, 30, "bob");
            Equal(new HackStats(Failed: 20), ledger.Squads(0)["gary"]);
            Equal(new HackStats(Pending: 30), ledger.Squads(0)["bob"]);
            ledger.Add(1, 10, 1, unit, 50, "bob");
            ledger.Complete(10, 1);
            Equal(new HackStats(Failed: 30), ledger.Squads(0)["bob"]);
            Equal(new HackStats(50, 0, 0, 1), ledger.Squads(1)["bob"]);
        }),
        ("Individual and grouped hacking amounts survive polling rounds selection and one-file export", () => {
            var history = new MatchHistory(); history.Begin(new(1, 2, 3), "Blue", "Red");
            var key = new UnitKey(0, 14); var empty = DamageModel.Group(Array.Empty<DamageSample>());
            for (var round = 1; round <= 2; round++)
            {
                var ledger = new ContributionLedger();
                ledger.Hacks.Add(1, 10, 0, key, 100 * round, "gary"); ledger.Hacks.Complete(10, 0);
                ledger.Hacks.Add(2, 11, 0, key, 40 * round, "bob"); ledger.Hacks.End(2, 11);
                var native = DamageModel.Group(new[] { new DamageSample(key, 0, 0, Squads: new[] {
                    new SquadStats("gary", "Hacker Gary", Hacked: 0), new SquadStats("bob", "Hacker Bob", Hacked: 0) }) });
                var enriched = ledger.Enrich(native, 0);
                history.Put(round, enriched, empty, true);
                history.Put(round, ledger.Enrich(native, 0), empty, true);
            }
            var overall = history.Overall().Left;
            var rows = StatsTable.Sort(StatsTable.Rows(overall, false, true, null, _ => "Hacker"), StatsColumn.FailedHack, true);
            Equal("bob", rows[0].Id); Equal(120L, rows[0].FailedHack); Equal(0L, rows[0].HackAmount);
            Equal(300L, rows[1].HackAmount); Equal(2L, rows[1].Hacked);
            Equal("gary", StatsTable.Sort(rows, StatsColumn.HackAmount, true)[0].Id);
            Equal(300L, UnitSelection.Rows(rows, "gary").Single().HackAmount);
            var grouped = StatsTable.Rows(overall, false, false, null, _ => "Hacker").Single();
            Equal(300L, grouped.HackAmount); Equal(120L, grouped.FailedHack); Equal(2L, grouped.Hacked);
            var saved = StatsFormat.Deserialize(StatsFormat.Serialize(StatsFormat.Capture(history, new(), _ => "Hacker", "test")));
            Equal(new HackStats(300, 0, 0, 2), saved.Overall.Left.Units.Single().Squads!.Single(s => s.Id == "gary").Hacking);
            Equal(new HackStats(0, 120), saved.Overall.Left.Units.Single().Squads!.Single(s => s.Id == "bob").Hacking);
            Equal(100L, saved.Rounds[0].Left.Units.Single().Squads!.Single(s => s.Id == "gary").Hacking!.Value.Successful);
            // A version-1 report from before the optional squad field remains readable.
            var json = JsonNode.Parse(StatsFormat.Serialize(saved))!;
            json["overall"]!["left"]!["units"]![0]!["squads"]![0]!.AsObject().Remove("hacking");
            Equal(null, StatsFormat.Deserialize(json.ToJsonString()).Overall.Left.Units.Single().Squads![0].Hacking);
            var legacy = new SquadStats("gary", "Hacker Gary", Hacked: 1);
            var mixed = SquadStats.Merge(new[] { legacy, new SquadStats("gary", "Hacker Gary", Hacking: new(100)) }).Single();
            Equal(null, mixed.Hacking);
            var unknown = StatsTable.Rows(DamageModel.Group(new[] { new DamageSample(key, 0, 0,
                Hacking: new(100), Squads: new[] { legacy }) }), false, true, null, _ => "Hacker").Single();
            Equal(null, unknown.HackAmount); Equal(null, unknown.FailedHack);
        }),
        ("Captured Hackers contribute hacking without duplicate grouped totals or hiding failed-only rows", () => {
            var key = new UnitKey(0, 14); var ledger = new ContributionLedger();
            ledger.Hacks.Add(1, 10, 0, key, 100, "gary"); ledger.Hacks.Complete(10, 0);
            ledger.Hacks.Add(2, 11, 0, key, 70, "bob"); ledger.Hacks.Complete(11, 0);
            ledger.Hacks.Add(2, 12, 0, key, 30, "bob"); ledger.Hacks.End(2, 12);
            var team = ledger.Enrich(DamageModel.Group(new[] { new DamageSample(key, 0, 0, Squads: new[] {
                new SquadStats("gary", "Hacker Gary", Hacked: 0),
                new SquadStats("bob", "Hacker Bob", ParentId: "gary", ParentUnit: key, Hacked: 0) }) }), 0);
            foreach (var individual in new[] { false, true })
            {
                var rows = StatsTable.Sort(StatsTable.Rows(team, false, individual, null, _ => "Hacker"), StatsColumn.HackAmount, true);
                Equal(170L, rows[0].HackAmount); Equal(30L, rows[0].FailedHack); Equal(2L, rows[0].Hacked);
                Equal(70L, rows[1].HackAmount); Equal(30L, rows[1].FailedHack); Equal(1L, rows[1].Hacked);
                Equal(170L, StatsTable.Sort(rows, StatsColumn.HackAmount, true)[0].HackAmount);
            }
            var id = "deployed:0:1";
            var failed = new StatsTableRow(id, key, false, "Gary", 0, 0, 0, 0, 0, 0, 0, 0, null) { HackAmount = 0, FailedHack = 50 };
            var opposite = failed with { Right = true, FailedHack = 0 };
            var visible = StatsTable.Sort(new[] { failed, opposite }, StatsColumn.FailedHack, true);
            Equal(1, visible.Length); Equal(false, visible[0].Right); Equal(50L, visible[0].FailedHack);
            var parent = failed with { Id = "parent", HackAmount = 100, FailedHack = 0 };
            var unknownChild = failed with { ParentId = "parent", HackAmount = null, FailedHack = null };
            Equal(null, StatsTable.Sort(new[] { parent, unknownChild }, StatsColumn.HackAmount, true)[0].HackAmount);
        })
    };
}
