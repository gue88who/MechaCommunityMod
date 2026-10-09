using MechaCommunityMod.Plugins.BattleStatistics;

internal static class TableChecks
{
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    internal static readonly (string, Action)[] All = {
        ("Spawned counts include idle units and distinct rounds without counting repeated snapshots", () => {
            var parent = new UnitKey(0, 1); var mine = new UnitKey(0, 2);
            TeamStats Team(params SquadStats[] children) => DamageModel.Group(new[] {
                new DamageSample(parent, 0, 0, Squads: new[] { new SquadStats("parent", "Spider") }),
                new DamageSample(mine, children.Sum(s => s.Damage), children.Sum(s => s.Kills), Squads: children) });
            SquadStats Spawn(string id, long damage) => new(id, "Mine", damage, ParentId: "parent", ParentUnit: parent, Spawned: true);
            var history = new MatchHistory(); history.Begin(new(1, 2, 3), "Blue", "Red");
            var empty = DamageModel.Group(Array.Empty<DamageSample>());
            var first = Team(Spawn("a", 10), Spawn("b", 0));
            history.Put(1, first, empty, true); history.Put(1, first, empty, true);
            history.Put(2, Team(Spawn("a", 10), Spawn("c", 30)), empty, true);
            string Name(UnitKey key) => key == mine ? "Spider Mine" : "Spider";
            var round = StatsTable.Rows(history.Find(1)!.Left, false, true, null, Name);
            Equal("Spider Mines x2", round.Single(r => r.Spawned).Name);
            var overall = StatsTable.Rows(history.Overall().Left, false, true, null, Name);
            var child = overall.Single(r => r.Spawned);
            Equal("Spider Mines x3", child.Name); Equal(3, child.MapIds.Length); Equal(50L, child.Damage);
            var regrouped = StatsTable.GroupAttributed(overall, Name);
            Equal("Spider Mines x3", regrouped.Single(r => r.Spawned).Name);
            Equal(50L, StatsTable.Sort(overall, StatsColumn.Damage, true).First().Damage);
            var groupedRound = StatsTable.Rows(history.Find(1)!.Left, false, false, null, Name);
            Equal("Spider x1", groupedRound.Single(r => r.ParentId is null).Name);
            Equal(true, groupedRound.All(r => r.Grouped && !r.HasLevelBadge));
        }),
        ("Selected unit groups and counts its children while captured unit selection stays individual", () => {
            var hacker = new UnitKey(0, 1); var vortex = new UnitKey(0, 2);
            var team = DamageModel.Group(new[] {
                new DamageSample(hacker, 10, 0, Squads: new[] { new SquadStats("gary", "Hacker Gary", 10) }),
                new DamageSample(vortex, 50, 0, Squads: new[] {
                    new SquadStats("isaac", "Vortex Isaac", 20, ParentId: "gary", ParentUnit: hacker),
                    new SquadStats("amanda", "Vortex Amanda", 30, ParentId: "gary", ParentUnit: hacker) }) });
            string Name(UnitKey key) => key == vortex ? "Vortex" : "Hacker";
            var all = StatsTable.Sort(StatsTable.Rows(team, false, true, null, Name, groupChildren: false), StatsColumn.Damage, true);
            var selected = UnitSelection.Rows(all, "gary");
            var grouped = StatsTable.Sort(StatsTable.GroupAttributed(selected, Name, attachedOnly: true), StatsColumn.Damage, true);
            Equal(2, grouped.Length); Equal("Hacker Gary", grouped[0].Name); Equal(60L, grouped[0].Damage);
            Equal("Vortexes x2", grouped[1].Name); Equal(50L, grouped[1].Damage);
            var captured = StatsTable.GroupAttributed(UnitSelection.Rows(all, "isaac"), Name, attachedOnly: true).Single();
            Equal("isaac", captured.Id); Equal("Vortex Isaac", captured.Name); Equal(20L, captured.Damage);
            Equal(true, captured.HasLevelBadge); Equal(false, grouped[1].HasLevelBadge);
            var both = StatsTable.GroupAttributed(selected.Concat(selected.Select(r => r with { Right = true })), Name, attachedOnly: true);
            Equal(2, both.Count(r => r.Name == "Vortexes x2"));
            Equal(1, both.Count(r => r.ParentId == "gary" && r.Right));
        }),
        ("Attributed units start collapsed and expand independently of source breakdowns", () => {
            var unit = new UnitKey(0, 1);
            StatsTableRow Row(string id, string? parent, long damage) => new(id, unit, false, id, 1, damage, 0, 2, 0, 0, 0, 0, 10,
                ParentId: parent) { CoreDamage = damage, DealtSources = new[] { new SourceDamage(DamageSource.Attack, damage, 0) } };
            var sorted = StatsTable.Sort(new[] { Row("parent", null, 10), Row("spawn", "parent", 20),
                Row("nested", "spawn", 30), Row("other", null, 5) }, StatsColumn.Damage, true);
            var expanded = new HashSet<(bool Right, string Id)>();
            var collapsed = StatsTable.Collapse(sorted, expanded);
            Equal("parent,other", string.Join(',', collapsed.Select(r => r.Id)));
            Equal(true, collapsed[0].HasAttributedUnits); Equal(false, collapsed[1].HasAttributedUnits);
            Equal(60L, collapsed[0].Damage); Equal(60L, collapsed[0].CoreDamage); Equal(3L, collapsed[0].Kills);
            var sourceDetails = StatsTable.Details(collapsed, new HashSet<(bool, string)> { (false, "parent") });
            Equal(3, sourceDetails.Length); Equal(true, sourceDetails[1].Breakdown);
            Equal(60L, sourceDetails[1].Damage);
            expanded.Add((false, "parent"));
            var oneLevel = StatsTable.Collapse(sorted, expanded);
            Equal("parent,spawn,other", string.Join(',', oneLevel.Select(r => r.Id)));
            Equal(true, oneLevel[1].HasAttributedUnits); Equal(1, oneLevel[1].Depth);
            Equal(50L, oneLevel[1].Damage); Equal(60L, oneLevel[0].Damage);
            expanded.Add((false, "spawn"));
            var nested = StatsTable.Collapse(sorted, expanded);
            Equal("parent,spawn,nested,other", string.Join(',', nested.Select(r => r.Id)));
            Equal(2, nested[2].Depth);
            expanded.Remove((false, "parent"));
            Equal("parent,other", string.Join(',', StatsTable.Collapse(sorted, expanded).Select(r => r.Id)));
            Equal(4, sorted.Length); Equal(60L, sorted[0].Damage);
        }),
        ("Child collapse isolates armies and keeps filtered or missing parents visible", () => {
            var unit = new UnitKey(0, 1);
            StatsTableRow Row(string id, bool right, string? parent) => new(id, unit, right, id, 0, 10, 0, 0, 0, 0, 0, 0, null, ParentId: parent);
            var sorted = StatsTable.Sort(new[] { Row("owner", false, null), Row("child", false, "owner"),
                Row("owner", true, null), Row("child", true, "owner"), Row("orphan", false, "missing") }, StatsColumn.Damage, true);
            var expanded = new HashSet<(bool, string)> { (false, "owner") };
            var shown = StatsTable.Collapse(sorted, expanded);
            Equal(1, shown.Count(r => r.Id == "child")); Equal(false, shown.Single(r => r.Id == "child").Right);
            Equal(0, shown.Single(r => r.Id == "orphan").Depth);
            var filtered = StatsTable.Collapse(sorted.Where(r => r.Id == "child"), new HashSet<(bool, string)>());
            Equal(2, filtered.Length); Equal(true, filtered.All(r => r.Depth == 0 && !r.HasAttributedUnits));
            var selected = UnitSelection.Rows(sorted, "child", true);
            Equal("child", StatsTable.Collapse(selected, new HashSet<(bool, string)>()).Single().Id);
        }),
        ("Core-only squads survive tables and child core totals count once", () => {
            var unit = new UnitKey(0, 1); var childUnit = new UnitKey(0, 2);
            var team = DamageModel.Group(new[] {
                new DamageSample(unit, 0, 0, Squads: new[] { new SquadStats("owner", "Owner", CoreDamage: 10) }),
                new DamageSample(childUnit, 0, 0, Squads: new[] {
                    new SquadStats("child1", "Child", ParentId: "owner", ParentUnit: unit, Spawned: true, CoreDamage: 20),
                    new SquadStats("child2", "Child", ParentId: "owner", ParentUnit: unit, Spawned: true, CoreDamage: 30) }) });
            var rows = StatsTable.Sort(StatsTable.Rows(team, false, true, null, _ => "Unit"), StatsColumn.CoreDamage, true);
            Equal(2, rows.Length); Equal(60L, rows[0].CoreDamage); Equal(50L, rows[1].CoreDamage);
            Equal(60L, StatsTable.Sort(rows, StatsColumn.CoreDamage, true)[0].CoreDamage);
            Equal(60L, UnitSelection.Rows(rows, "owner")[0].CoreDamage);
            var unknown = rows[1] with { CoreDamage = null, Inclusive = false };
            Equal(null, StatsTable.Sort(new[] { rows[0] with { CoreDamage = 10, Inclusive = false }, unknown }, StatsColumn.CoreDamage, true)[0].CoreDamage);
            var grouped = StatsTable.Sort(StatsTable.Rows(team, false, false, null, _ => "Unit"), StatsColumn.CoreDamage, true);
            Equal(60L, grouped.Single(r => r.Depth == 0).CoreDamage);
            var spell = new StatsTableRow("spell", new(102, 1), false, "Summon", 0, 0, 0, 0, 0, null, null, null, null);
            var summon = rows[1] with { ParentId = "spell", CoreDamage = 50, Inclusive = false };
            var cast = StatsTable.Sort(new[] { spell, summon }, StatsColumn.CoreDamage, true)[0];
            Equal(50L, cast.CoreDamage); Equal(true, StatsTable.Useful(cast));
            var unobservedSummon = summon with { Id = "unobserved", CoreDamage = null };
            Equal(null, StatsTable.Sort(new[] { spell, unobservedSummon, summon }, StatsColumn.CoreDamage, true)[0].CoreDamage);
        }),
        ("Selected squad round list excludes rounds before purchase and after sale", () => {
            var history = new MatchHistory(); history.Begin(new(1, 2, 3), "Left", "Right");
            var costs = new UnitCostHistory(); var boards = new BoardHistory();
            var unit = new UnitKey(0, 1); var empty = DamageModel.Group(Array.Empty<DamageSample>());
            history.Put(1, DamageModel.Group(new[] { new DamageSample(unit, 10, 1, Squads: new[] { new SquadStats("other", "Other", 10, 1) }) }), empty, true);
            history.Put(2, empty, empty, true);
            costs.Put(2, new(), new(), new() { ["selected"] = new(100, 0, 0) }, new());
            history.Put(3, empty, DamageModel.Group(new[] { new DamageSample(unit, 20, 1, Squads: new[] { new SquadStats("selected", "Selected", 20, 1) }) }), true);
            history.Put(4, empty, empty, true);
            var rounds = UnitSelection.AvailableRounds(history, costs, boards, "selected");
            Equal("2,3", string.Join(',', rounds));
            Equal(0, UnitSelection.AvailableRounds(history, costs, boards, "not-present").Length);
        }),
        ("Battlefield selection isolates one squad and its descendants including captured units", () => {
            var hacker = new UnitKey(0, 14); var vortex = new UnitKey(0, 2);
            var team = DamageModel.Group(new[] {
                new DamageSample(hacker, 10, 0, Squads: new[] { new SquadStats("gary", "Hacker Gary", 10) }),
                new DamageSample(vortex, 50, 2, Squads: new[] {
                    new SquadStats("a", "Vortex A", 20, 1, ParentId: "gary", ParentUnit: hacker),
                    new SquadStats("b", "Vortex B", 30, 1, ParentId: "gary", ParentUnit: hacker) }) });
            var rows = StatsTable.Sort(StatsTable.Rows(team, false, true, null, _ => "Unit", groupChildren: false), StatsColumn.Damage, true);
            var owner = UnitSelection.Rows(rows, "gary");
            Equal(3, owner.Length); Equal(60L, owner[0].Damage); Equal(0, owner[0].Depth);
            var child = UnitSelection.Rows(rows, "a");
            Equal(1, child.Length); Equal(20L, child[0].Damage); Equal(0, child[0].Depth);
            Equal(0, UnitSelection.Rows(rows, "unknown").Length);
            var captured = child[0] with { Right = true, Damage = 7 };
            Equal(7L, UnitSelection.Rows(child.Concat(new[] { captured }), "a", true).Single().Damage);
            Equal(20L, UnitSelection.Rows(child, "a", true).Single().Damage);
        }),
        ("Individual Hacker conversions survive rounds table sorting and saved reports", () => {
            var key = new UnitKey(0, 14);
            var history = new MatchHistory(); history.Begin(new(1, 2, 3), "Left", "Right");
            var empty = DamageModel.Group(Array.Empty<DamageSample>());
            for (var round = 1; round <= 2; round++)
                history.Put(round, DamageModel.Group(new[] { new DamageSample(key, 0, 0,
                    Hacking: new HackStats(Converted: 3), Squads: new[] {
                        new SquadStats("gary", "Hacker Gary", Hacked: 2),
                        new SquadStats("bob", "Hacker Bob", Hacked: 1) }) }), empty, true);
            var rows = StatsTable.Sort(StatsTable.Rows(history.Overall().Left, false, true, null, _ => "Hacker"), StatsColumn.Hacked, true);
            Equal("gary", rows[0].Id); Equal(4L, rows[0].Hacked); Equal(2L, rows[1].Hacked);
            Equal(6L, StatsTable.Rows(history.Overall().Left, false, false, null, _ => "Hacker").Single().Hacked);
            var document = StatsFormat.Deserialize(StatsFormat.Serialize(StatsFormat.Capture(history, new(), _ => "Hacker", "test")));
            Equal(4L, document.Overall.Left.Units.Single().Squads!.Single(s => s.Id == "gary").Hacked);
            var renamed = rows.Select(r => r with { Name = r.Id == "gary" ? "ZZZ" : "AAA", Damage = 0 });
            Equal("bob", StatsTable.Sort(renamed, StatsColumn.Damage, true)[0].Id);
            Equal("bob", StatsTable.Sort(renamed.Select(r => r with { Name = "different" }), StatsColumn.Damage, true)[0].Id);
        }),
        ("Spell summons group under counted casts without duplicating army totals", () => {
            var spell = new UnitKey(102, 1200001); var crawler = new UnitKey(0, 10);
            var team = DamageModel.Group(new[] {
                new DamageSample(spell, 0, 0, Casts: 6),
                new DamageSample(crawler, 35, 3, Squads: new[] {
                    new SquadStats("a", "Crawler", 10, 1, ParentId: $"type:{spell}", ParentUnit: spell, Spawned: true),
                    new SquadStats("b", "Crawler", 20, 2, ParentId: $"type:{spell}", ParentUnit: spell, Spawned: true),
                    new SquadStats("gary", "Crawler Gary", 5) }) });
            foreach (var individual in new[] { false, true })
            {
                var rows = StatsTable.Sort(StatsTable.Rows(team, false, individual, null,
                    k => k == spell ? "Underground Threat" : "Crawler"), StatsColumn.Damage, true);
                var parent = rows.Single(r => r.Unit == spell);
                Equal("Underground Threat x6", parent.Name); Equal(30L, parent.Damage);
                Equal(3L, parent.Kills); Equal(true, StatsTable.Useful(parent));
                Equal(1, rows.Count(r => r.ParentId == parent.Id));
                Equal("Crawlers x2", rows.Single(r => r.ParentId == parent.Id).Name);
                Equal(35L, rows.Where(r => r.Depth == 0).Sum(r => r.Damage ?? 0));
            }
        }),
        ("Hacker children group by unit type and owner with counts instead of nicknames", () => {
            var hacker = new UnitKey(0, 1); var vortex = new UnitKey(0, 2);
            var team = DamageModel.Group(new[] {
                new DamageSample(hacker, 10, 0, Squads: new[] { new SquadStats("gary", "Hacker Gary", 10), new SquadStats("bob", "Hacker Bob") }),
                new DamageSample(vortex, 60, 3, Squads: new[] {
                    new SquadStats("isaac", "Vortex Isaac", 20, 1, ParentId: "gary", ParentUnit: hacker),
                    new SquadStats("amanda", "Vortex Amanda", 30, 1, ParentId: "gary", ParentUnit: hacker),
                    new SquadStats("daniel", "Vortex Daniel", 10, 1, ParentId: "bob", ParentUnit: hacker) }) });
            var rows = StatsTable.Rows(team, false, true, null, k => k == vortex ? "Vortex" : "Hacker");
            Equal("Vortexes x2", rows.Single(r => r.ParentId == "gary").Name);
            Equal(50L, rows.Single(r => r.ParentId == "gary").Damage);
            Equal("Vortex x1", rows.Single(r => r.ParentId == "bob").Name);
            var sorted = StatsTable.Sort(rows, StatsColumn.Damage, true);
            Equal(60L, sorted[0].Damage); Equal(70L, sorted.Where(r => r.Depth == 0).Sum(r => r.Damage ?? 0));
        }),
        ("Individual squad supply uses actual round valuations without repeated technology costs", () => {
            var key = new UnitKey(0, 1);
            var team = DamageModel.Group(new[] { new DamageSample(key, 30, 0, Squads: new[] {
                new SquadStats("gary", "Gary", 10), new SquadStats("bob", "Bob", 20) }) });
            var history = new UnitCostHistory();
            var type = new Dictionary<UnitKey, UnitCost> { [key] = new(200, 50, 300) };
            history.Put(1, type, new(), new() { ["gary"] = new(100, 0, 0), ["bob"] = new(100, 50, 0) });
            history.Put(2, type, new(), new() { ["gary"] = new(100, 100, 0) });
            var first = StatsTable.Rows(team, false, true, type, _ => "Melter", squadCosts: history.GetSquads(1, false));
            Equal(100L, first.Single(r => r.Id == "gary").Supply); Equal(150L, first.Single(r => r.Id == "bob").Supply);
            Equal(200L, history.GetSquads(0, false)["gary"].Total); Equal(150L, history.GetSquads(0, false)["bob"].Total);
            history.Resume(1); Equal(100L, history.GetSquads(0, false)["gary"].Total);
            Equal(0, history.GetSquads(1, true).Count);
        }),
        ("Empty support spells are omitted but spell parents with child damage remain useful", () => {
            var shield = new StatsTableRow("shield", new(102, 1), false, "Shield Drop", 0, 0, 0, 0, 0, 0, 0, 0, null);
            Equal(false, StatsTable.Useful(shield)); Equal(true, StatsTable.Useful(shield with { Damage = 100 }));
            var mine = shield with { Id = "mine", Unit = new(0, 2), ParentId = "shield", Damage = 50 };
            var sorted = StatsTable.Sort(new[] { shield, mine }, StatsColumn.Damage, true);
            Equal(true, StatsTable.Useful(sorted[0])); Equal(50L, sorted[0].Damage);
        }),
        ("Standalone spell rows do not expand into duplicate self-breakdowns", () => {
            var source = new DamageSource(DamageCategory.Spell, 7, "Incendiary Bomb");
            var team = DamageModel.Group(new[] { new DamageSample(new(102, 7), 100, 2,
                DealtSources: new[] { new SourceDamage(source, 100, 0) }) });
            var row = StatsTable.Rows(team, false, false, null, _ => "Incendiary Bomb").Single();
            Equal(false, row.HasDetails);
            var details = StatsTable.Details(new[] { row }, new HashSet<(bool, string)> { (false, row.Id) });
            Equal(1, details.Length); Equal(100L, details[0].Damage);
        }),
        ("Expanded technologies preserve both damage channels without adding to parent totals", () => {
            var tech = new DamageSource(DamageCategory.Tech, 7, "Explosion");
            var key = new UnitKey(0, 1);
            var team = DamageModel.Group(new[] { new DamageSample(key, 30, 1, 50, 10, 20,
                DealtSources: new[] { new SourceDamage(tech, 30, 20) }, TakenSources: new[] { new SourceDamage(tech, 40, 10) }) });
            var rows = StatsTable.Sort(StatsTable.Rows(team, false, false, null, _ => "Spider"), StatsColumn.Damage, true);
            var expanded = StatsTable.Details(rows, new HashSet<(bool, string)> { (false, rows[0].Id) });
            Equal(3, expanded.Length); Equal(true, expanded[1].Breakdown); Equal(tech, expanded[1].Source);
            Equal(30L, expanded[0].Damage); Equal(30L, expanded[1].Damage);
            Equal(40L, expanded[2].Tanked); Equal(10L, expanded[2].TankedOverkill);
            Equal(false, expanded[1].AccentRight); Equal(true, expanded[2].AccentRight);
            Equal(true, expanded[1].Tanked is null); Equal(true, expanded[2].Damage is null);
            Equal(true, expanded[1].Kills is null); Equal(1, expanded[1].Depth);
        }),
        ("Technology breakdown includes spawned sources and controlled units stay under their owner", () => {
            var tech = new DamageSource(DamageCategory.Tech, 8, "Mine explosion");
            var parent = new StatsTableRow("gary", new(0, 1), false, "Gary", 0, 10, 0, 0, 0, 0, 0, 0, null);
            var child = parent with { Id = "child", Unit = new(0, 2), ParentId = "gary", Damage = 20,
                DealtSources = new[] { new SourceDamage(tech, 20, 0) } };
            var sorted = StatsTable.Sort(new[] { parent, child }, StatsColumn.Damage, true);
            Equal(30L, sorted[0].Damage); Equal(1, sorted[1].Depth);
            var details = StatsTable.Details(sorted, new HashSet<(bool, string)> { (false, "gary") });
            Equal(20L, details.Single(r => r.Source == tech).Damage); Equal(30L, details[0].Damage);
        }),
        ("Buildings stay one unnamed type row across rounds with individual units enabled", () => {
            var unit = new UnitKey(1, 7);
            var history = new MatchHistory(); history.Begin(new(1, 2, 3), "Left", "Right");
            var empty = DamageModel.Group(Array.Empty<DamageSample>());
            foreach (var round in new[] { 1, 2 })
                history.Put(round, DamageModel.Group(new[] { new DamageSample(unit, round * 100, round,
                    Squads: new[] { new SquadStats("old-building-" + round, "Building Gary " + round, round * 100, round) }) }), empty, true);
            var rows = StatsTable.Rows(history.Overall().Left, false, true, null, _ => "Missile Tower", key => key.Kind != 1);
            Equal(1, rows.Length); Equal("Missile Tower", rows[0].Name); Equal(300L, rows[0].Damage); Equal(3L, rows[0].Kills);
            Equal("Missile Tower", StatsTable.Rows(history.Find(1)!.Left, false, true, null, _ => "Missile Tower", key => key.Kind != 1).Single().Name);
        }),
        ("Spawned mines stay with their exact spawning squad and have no independent nickname", () => {
            var unit = new UnitKey(0, 1); var mine = new UnitKey(0, 2);
            var team = DamageModel.Group(new[] {
                new DamageSample(unit, 30, 0, Squads: new[] { new SquadStats("gary", "Spider Gary", 10), new SquadStats("bob", "Spider Bob", 20) }),
                new DamageSample(mine, 900, 9, Squads: new[] {
                    new SquadStats("m1", "Spider Mine", 300, 3, ParentId: "gary", ParentUnit: unit, Spawned: true),
                    new SquadStats("m2", "Spider Mine", 200, 2, ParentId: "gary", ParentUnit: unit, Spawned: true),
                    new SquadStats("m3", "Spider Mine", 400, 4, ParentId: "bob", ParentUnit: unit, Spawned: true) }) });
            var rows = StatsTable.Rows(team, false, true, null, k => k == unit ? "Spider" : "Spider Mine");
            Equal(4, rows.Length); Equal(930L, rows.Sum(r => r.Damage ?? 0));
            Equal(500L, rows.Single(r => r.ParentId == "gary").Damage);
            Equal(400L, rows.Single(r => r.ParentId == "bob").Damage);
            Equal("Spider Mines x2", rows.Single(r => r.ParentId == "gary").Name);
            Equal("Spider Mine x1", rows.Single(r => r.ParentId == "bob").Name);
            var sorted = StatsTable.Sort(rows, StatsColumn.Damage, true);
            Equal("gary", sorted[0].Id); Equal("gary", sorted[1].ParentId); Equal(1, sorted[1].Depth);
            Equal(510L, sorted[0].Damage); Equal(500L, sorted[1].Damage);
            Equal("bob", sorted[2].Id); Equal("bob", sorted[3].ParentId); Equal(420L, sorted[2].Damage);
            Equal(930L, sorted.Where(r => r.Depth == 0).Sum(r => r.Damage ?? 0));
            Equal(510L, StatsTable.Sort(sorted, StatsColumn.Damage, true)[0].Damage);
            var grouped = StatsTable.Sort(StatsTable.Rows(team, false, false, null, _ => "Spider"), StatsColumn.Damage, true);
            Equal(2, grouped.Length); Equal(930L, grouped.Where(r => r.Depth == 0).Sum(r => r.Damage ?? 0)); Equal(1, grouped[1].Depth);
        }),
        ("Child hierarchy never crosses armies or hides missing and cyclic parent links", () => {
            var parent = new StatsTableRow("p", new(0, 1), false, "Gary", 1, 10, 0, 0, 0, 0, 0, 0, null);
            var child = parent with { Id = "c", Name = "Mine", Right = true, ParentId = "p", Spawned = true };
            var sorted = StatsTable.Sort(new[] { parent, child }, StatsColumn.Damage, true);
            Equal(2, sorted.Length); Equal(0, sorted.Single(r => r.Id == "c").Depth);
            var cycle = StatsTable.Sort(new[] { parent with { ParentId = "c" }, child with { Right = false } }, null, false);
            Equal(2, cycle.Length);
        }),
        ("Spawn parent provenance survives round aggregation and saved report serialization", () => {
            var key = new UnitKey(0, 2); var parent = new UnitKey(0, 1);
            var squad = new SquadStats("mine", "Spider Mine", 20, ParentId: "gary", ParentUnit: parent, Spawned: true);
            var history = new MatchHistory(); history.Begin(new(1, 2, 3), "Left", "Right");
            var team = DamageModel.Group(new[] { new DamageSample(key, 20, 0, Squads: new[] { squad }) });
            var empty = DamageModel.Group(Array.Empty<DamageSample>());
            history.Put(1, team, empty, true); history.Put(2, team, empty, true);
            var merged = history.Overall().Left.Rows.Single().Squads!.Single();
            Equal(40L, merged.Damage); Equal("gary", merged.ParentId); Equal(true, merged.Spawned);
            var document = StatsFormat.Deserialize(StatsFormat.Serialize(StatsFormat.Capture(history, new UnitCostHistory(), _ => "Mine", "test")));
            var saved = document.Overall.Left.Units.Single().Squads!.Single();
            Equal("gary", saved.ParentId); Equal(parent, saved.ParentUnit); Equal(true, saved.Spawned);
        }),
        ("Dragging table content moves both axes and clamps at every edge", () => {
            Equal((70f, 60f), TablePanning.Move(50, 40, -20, -20, 100, 100));
            Equal((30f, 20f), TablePanning.Move(50, 40, 20, 20, 100, 100));
            Equal((0f, 0f), TablePanning.Move(50, 40, 200, 200, 100, 100));
            Equal((100f, 100f), TablePanning.Move(50, 40, -200, -200, 100, 100));
            Equal((0f, 0f), TablePanning.Move(50, 40, -200, -200, 0, 0));
        }),
        ("Table individual rows preserve partial damage, tanked and XP without duplicating shared values", () => {
            var key = new UnitKey(0, 1);
            var team = DamageModel.Group(new[] { new DamageSample(key, 100, 5, 120, 20, 30,
                Hacking: new HackStats(Converted: 3), Squads: new[] { new SquadStats("gary", "Melter Gary", 70, 3, 80, 20, 10, 1.25, 2.5) }) });
            var rows = StatsTable.Rows(team, false, true, new Dictionary<UnitKey, UnitCost> { [key] = new(200, 50, 100) }, _ => "Melter");
            Equal(2, rows.Length);
            Equal(100L, rows.Sum(r => r.Damage ?? 0)); Equal(30L, rows.Sum(r => r.DamageOverkill ?? 0));
            Equal(100L, rows.Sum(r => r.Tanked ?? 0)); Equal(20L, rows.Sum(r => r.TankedOverkill ?? 0));
            Equal(5L, rows.Sum(r => r.Kills ?? 0)); Equal(1.25, rows.Sum(r => r.XpGained ?? 0)); Equal(2.5, rows.Sum(r => r.XpFed ?? 0));
            Equal(false, rows.Any(r => r.Shared));
            Equal(3L, team.Rows.Single().Hacking.Converted);
            Equal(true, rows.All(r => r.Supply is null && r.Hacked is null));
            Equal(true, rows.Single(r => r.Id == "gary").Supply is null);
        }),
        ("Table preserves cost-only units and does not invent supply for spell rows", () => {
            var spell = new UnitKey(102, 7); var unit = new UnitKey(0, 4);
            var team = DamageModel.Group(new[] { new DamageSample(spell, 40, 2) });
            var rows = StatsTable.Rows(team, true, false, new Dictionary<UnitKey, UnitCost> { [unit] = new(100, 20, 30) }, k => k.ToString());
            Equal(2, rows.Length); Equal(150L, rows.Single(r => r.Unit == unit).Supply);
            Equal(true, rows.Single(r => r.Unit == spell).Supply is null);
        }),
        ("Empty enemy copies are excluded while idle owned squads and conversion stats survive", () => {
            var red = new StatsTableRow("deployed:0:18", new(0, 3), true, "Vulcan Leo", 1, 100, 0, 50, 0, 0, 2, 1, 400);
            var ghost = red with { Right = false, Kills = 0, Damage = 0, Tanked = 0, XpGained = 0, XpFed = 0, Supply = null };
            Equal(1, StatsTable.Sort(new[] { red, ghost }, StatsColumn.Damage, true).Length);
            Equal(2, StatsTable.Sort(new[] { red, ghost with { Supply = 400 } }, StatsColumn.Damage, true).Length);
            Equal(2, StatsTable.Sort(new[] { red, ghost with { Damage = 10 } }, StatsColumn.Damage, true).Length);
            Equal(1, StatsTable.Sort(new[] { ghost }, StatsColumn.Damage, true).Length);
        }),
        ("Incoming sources retain friendly fire ownership and separate both players", () => {
            var spell = new DamageSource(DamageCategory.Spell, 1, "Orbital Javelin");
            var sources = DamageSources.Merge(new[] { new SourceDamage(spell, 50, 0, false), new SourceDamage(spell, 20, 0, true) });
            Equal(2, sources.Length);
            var unit = new StatsTableRow("fort", new(0, 1), false, "Fortress", 0, 0, 0, 70, 0, 0, 0, 0, null) { TakenSources = sources };
            var details = StatsTable.Details(new[] { unit }, new HashSet<(bool, string)> { (false, "fort") });
            Equal(false, details.Single(r => r.Tanked == 50).AccentRight);
            Equal(true, details.Single(r => r.Tanked == 20).AccentRight);
        }),
        ("Every numeric table column sorts both ways with unavailable values last", () => {
            var low = new StatsTableRow("a", new(0, 1), false, "A", 1, 2, 3, 4, 5, 6, 1.25, 2.25, 7) { CoreDamage = 8, HackAmount = 9, FailedHack = 10 };
            var high = new StatsTableRow("b", new(0, 2), true, "B", 10, 20, 30, 40, 50, 60, 12.5, 22.5, 70) { CoreDamage = 80, HackAmount = 90, FailedHack = 100 };
            var missing = new StatsTableRow("c", new(0, 3), false, "C", null, null, null, null, null, null, null, null, null);
            foreach (var column in Enum.GetValues<StatsColumn>()) {
                var ascending = StatsTable.Sort(new[] { high, missing, low }, column, false);
                var descending = StatsTable.Sort(new[] { low, missing, high }, column, true);
                Equal("a", ascending[0].Id); Equal("b", descending[0].Id);
                Equal("c", ascending[2].Id); Equal("c", descending[2].Id);
            }
            Equal("a", StatsTable.Sort(new[] { high, low }, null, false)[0].Id);
            Equal("b", StatsTable.Sort(new[] { low, high }, null, true)[0].Id);
            Equal(false, StatsTable.Sort(new[] { high, low }, null, false, true)[0].Right);
            Equal(true, StatsTable.Sort(new[] { low, high }, null, true, true)[0].Right);
        }),
        ("Table refuses inconsistent squad totals instead of inflating statistics", () => {
            var row = new DamageRow(new(0, 1), 10, 1, 20) { Squads = new[] { new SquadStats("a", "Gary", 100, 10, 200) } };
            var rows = StatsTable.Rows(new(new[] { row }, 10, 1, 20), false, true, null, _ => "Melter");
            Equal(1, rows.Length); Equal(10L, rows[0].Damage); Equal(20L, rows[0].Tanked);
        })
    };
}
