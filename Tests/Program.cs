using MechaCommunityMod.Plugins.BattleStatistics;
using MechaCommunityMod;

var checks = new (string Name, Action Run)[]
{
    ("One area spell cast counts effective damage only and one use across victims", () =>
    {
        var store = new DamageSourceStore(); var spell = new DamageSource(DamageCategory.Spell, 10, "Strike");
        store.Cast(1, 0, spell, 500);
        var hit = new OverkillHit(1);
        hit.Observe(10, 100, 1000, 100); hit.Observe(11, 300, 1000, 300);
        store.AddUnowned(1, 0, spell, 100, hit.Consume(10, 1000), 1);
        store.AddUnowned(1, 0, spell, 300, hit.Consume(11, 1000), 1);
        var s = store.Unowned(1, 0).Single();
        Equal(1L, s.Casts); Equal(400L, s.Damage); Equal(1600L, s.Overkill); Equal(2L, s.Kills);
        var row = DamageModel.Group(new[] { new DamageSample(new UnitKey(102, 10), s.Damage, s.Kills,
            DealtOverkill: s.Overkill, DealtSources: new[] { new SourceDamage(spell, s.Damage, s.Overkill) }, Casts: s.Casts) }).Rows.Single();
        Equal(400L, row.TotalDealt); Equal(1L, row.Casts);
        Equal(0L, DamageBreakdown.Rows(row, true).Single(r => !r.Category).Overkill);
    }),
    ("Mixed attacks techs and spells exclude only spell excess on both sides and in history", () =>
    {
        var spell = new DamageSource(DamageCategory.Spell, 10, "Strike");
        var tech = new DamageSource(DamageCategory.Tech, 11, "Explosion");
        var sources = new[] { new SourceDamage(DamageSource.Attack, 100, 20),
            new SourceDamage(tech, 200, 30), new SourceDamage(spell, 300, 900) };
        var stats = DamageModel.Group(new[] { new DamageSample(new UnitKey(0, 1), 600, 3,
            DamageTaken: 1550, Overkill: 950, DealtOverkill: 950, DealtSources: sources, TakenSources: sources) });
        var row = stats.Rows.Single();
        Equal(650L, stats.TotalDealt); Equal(650L, stats.TotalDamageTaken);
        Equal(50L, row.DealtOverkill); Equal(50L, row.Overkill);
        foreach (var dealt in new[] { true, false })
        {
            Equal(300L, row.Sources(dealt).Single(s => s.Source == spell).Damage);
            Equal(0L, row.Sources(dealt).Single(s => s.Source == spell).Overkill);
            Equal(30L, row.Sources(dealt).Single(s => s.Source == tech).Overkill);
        }
        var history = NewHistory(); var empty = DamageModel.Group(Array.Empty<DamageSample>());
        history.Put(1, stats, empty, true); history.Put(2, stats, empty, true);
        Equal(1300L, history.Overall().Left.TotalDealt);
        Equal(1300L, history.Overall().Left.TotalDamageTaken);
        Equal(100L, history.Overall().Left.Rows.Single().Overkill);
        Equal(900L, sources.Single(s => s.Source == spell).Overkill); // raw observation remains immutable
    }),
    ("Spell cast counts ignore repeated notifications and include separate releases and zero damage", () =>
    {
        var store = new DamageSourceStore(); var spell = new DamageSource(DamageCategory.Spell, 20, "Shield");
        store.Cast(1, 0, spell, 500); store.Cast(1, 0, spell, 500); store.Cast(1, 0, spell, 501);
        var s = store.Unowned(1, 0).Single(); Equal(2L, s.Casts); Equal(0L, s.Damage); Equal(0L, s.Overkill);
        Equal(0, store.Unowned(1, 1).Count());
        store.Cast(1, 1, spell, 500); Equal(1L, store.Unowned(1, 1).Single().Casts);
    }),
    ("Spell uses sum rounds once and remain in zero-damage result rows", () =>
    {
        var history = NewHistory(); var key = new UnitKey(102, 20);
        var first = DamageModel.Group(new[] { new DamageSample(key, 0, 0, Casts: 1) });
        var second = DamageModel.Group(new[] { new DamageSample(key, 30, 0, Casts: 2) });
        var empty = DamageModel.Group(Array.Empty<DamageSample>());
        history.Put(1, first, empty, true); history.Put(1, first, empty, true); history.Put(2, second, empty, true);
        Equal(1L, history.Find(1)!.Left.Rows.Single().Casts);
        Equal(3L, history.Overall().Left.Rows.Single().Casts);
        Equal(30L, history.Overall().Left.TotalDamage);
    }),
    ("Spell casts reset with replay rewinds and new matches", () =>
    {
        var store = new DamageSourceStore(); var identity = new MatchIdentity(1, 2, 3);
        var spell = new DamageSource(DamageCategory.Spell, 10, "Strike");
        store.Begin(identity); store.Cast(1, 0, spell, 500); store.Cast(2, 0, spell, 500);
        store.RetainRounds(new[] { 1L }); store.Begin(identity);
        Equal(1L, store.Unowned(1, 0).Single().Casts); Equal(0, store.Unowned(2, 0).Count());
        store.Cast(2, 0, spell, 500); Equal(1L, store.Unowned(2, 0).Single().Casts);
        store.Begin(new MatchIdentity(4, 5, 6)); Equal(0, store.Unowned(1, 0).Count());
    }),
    ("Native conversion target cleanup does not turn a successful hack into failure", () =>
    {
        var c = new ContributionLedger(); var hacker = new UnitKey(0, 7); var target = new UnitKey(0, 2);
        c.Hacks.Add(1, 2, 0, hacker, 120);
        c.Hacks.BeginConversion(2);
        c.Hacks.End(1, 2); // OpponentController.Remove stops the beam before ChangeTeam returns.
        Equal(new HackStats(0, 0, 120, 0), c.Hacks.Snapshot(0)[hacker]);
        c.Convert(2, target, 0);
        c.Hacks.EndConversion(2);
        c.Hacks.Died(1); c.Hacks.Died(2); c.Hacks.Finish();
        Equal(new HackStats(120, 0, 0, 1), c.Hacks.Snapshot(0)[hacker]);
        c.Hit(2, 0, target, true, 10, 0);
        Equal(10L, c.Enrich(DamageModel.Group(Array.Empty<DamageSample>()), 0).Rows.Single(r => r.Unit == hacker).Contributions!.Single().Damage);
    }),
    ("Aborted conversion flushes focus losses and does not rescue earlier failed attempts", () =>
    {
        var h = new HackLedger(); var hacker = new UnitKey(0, 7);
        h.Add(1, 2, 0, hacker, 20); h.End(1, 2);
        h.Add(1, 2, 0, hacker, 30); h.BeginConversion(2); h.End(1, 2); h.EndConversion(2);
        Equal(new HackStats(0, 50, 0, 0), h.Snapshot(0)[hacker]);
        h.Add(1, 2, 0, hacker, 40); h.BeginConversion(2); h.End(1, 2); h.Complete(2, 0); h.EndConversion(2);
        Equal(new HackStats(40, 50, 0, 1), h.Snapshot(0)[hacker]);
    }),
    ("Cooperating and opposing Hackers settle correctly during conversion cleanup", () =>
    {
        var h = new HackLedger(); var hacker = new UnitKey(0, 7);
        h.Add(1, 4, 0, hacker, 20); h.Add(2, 4, 0, hacker, 30); h.Add(3, 4, 1, hacker, 40);
        h.BeginConversion(4); h.End(1, 4); h.End(2, 4); h.End(3, 4);
        h.Complete(4, 0); h.EndConversion(4);
        Equal(new HackStats(50, 0, 0, 1), h.Snapshot(0)[hacker]);
        Equal(new HackStats(0, 40, 0, 0), h.Snapshot(1)[hacker]);
    }),
    ("Temporary Spider Mine recorders supply dealt, taken and kills omitted by native unit list", () =>
    {
        var store = new LateUnitStats(); var mine = new UnitKey(0, 1002);
        store.Add(10, 0, 100, mine, 120, 0, 2); store.Add(10, 0, 100, mine, 0, 50, 0);
        store.Add(10, 0, 101, mine, 30, 20, 1);
        var result = DamageModel.Group(store.Read(10, 0, new HashSet<long>()).Select(r => r.Sample));
        Equal(150L, result.TotalDamage); Equal(70L, result.TotalDamageTaken); Equal(3L, result.TotalKills);
        Equal(1002, result.Rows.Single().Unit.Id);
        Equal(150L, DamageModel.Group(store.Read(10, 0, new HashSet<long>()).Select(r => r.Sample)).TotalDamage);
        // A recorder promoted into the native list is consumed only there.
        Equal(30L, DamageModel.Group(store.Read(10, 0, new HashSet<long> { 100 }).Select(r => r.Sample)).TotalDamage);
    }),
    ("Temporary recorder damage stays with event teams through conversions and survives round snapshots", () =>
    {
        var store = new LateUnitStats(); var unit = new UnitKey(0, 1002); var identity = new MatchIdentity(1, 2, 3);
        store.Begin(identity); store.Add(10, 0, 100, unit, 40, 20, 0); store.Add(10, 1, 100, unit, 60, 30, 1);
        store.Add(11, 0, 101, unit, 999, 0, 0); store.RetainRounds(new[] { 10L }); store.Begin(identity);
        Equal(40L, store.Read(10, 0, new HashSet<long>()).Single().Sample.Damage);
        Equal(60L, store.Read(10, 1, new HashSet<long>()).Single().Sample.Damage);
        Equal(0, store.Read(11, 0, new HashSet<long>()).Count());
        store.Begin(new MatchIdentity(4, 5, 6)); Equal(0, store.Read(10, 0, new HashSet<long>()).Count());
    }),
    ("Gifted squads have full base and level value while shared technology counts once", () =>
    {
        var unit = new UnitKey(0, 5);
        var value = UnitCost.Deployment(new[] { (unit, 200L, 0L, 300L), (unit, 200L, 100L, 300L), (unit, 200L, 300L, 300L) });
        Equal(1300L, value[unit].Total); Equal(600L, value[unit].Purchase); Equal(300L, value[unit].Technology);
    }),
    ("Completed hacking stays successful after target and Hacker die", () =>
    {
        var h = new HackLedger(); var unit = new UnitKey(0, 7);
        h.Add(1, 2, 0, unit, 120); h.Add(1, 2, 0, unit, 80);
        Equal(200L, h.Snapshot(0)[unit].Pending);
        h.Complete(2, 0); h.Died(1); h.Died(2); h.Finish();
        Equal(new HackStats(200, 0, 0, 1), h.Snapshot(0)[unit]);
    }),
    ("Hacker death, target death, focus loss and round end settle unsuccessful progress", () =>
    {
        var h = new HackLedger(); var unit = new UnitKey(0, 7);
        h.Add(1, 2, 0, unit, 10); h.Died(1);
        h.Add(3, 4, 0, unit, 20); h.Died(4);
        h.Add(5, 6, 0, unit, 30); h.End(5, 6);
        h.Add(7, 8, 0, unit, 40); h.Finish();
        Equal(new HackStats(0, 100, 0, 0), h.Snapshot(0)[unit]);
    }),
    ("Abandoned hacking is not retroactively successful on a later attempt", () =>
    {
        var h = new HackLedger(); var unit = new UnitKey(0, 7);
        h.Add(1, 2, 0, unit, 100); h.End(1, 2);
        h.Add(1, 2, 0, unit, 200); h.Complete(2, 0);
        Equal(new HackStats(200, 100, 0, 1), h.Snapshot(0)[unit]);
    }),
    ("Cooperating Hackers share successful progress but count a conversion once", () =>
    {
        var h = new HackLedger(); var unit = new UnitKey(0, 7);
        h.Add(1, 3, 0, unit, 100); h.Add(2, 3, 0, unit, 200); h.Complete(3, 0);
        Equal(new HackStats(300, 0, 0, 1), h.Snapshot(0)[unit]);
    }),
    ("Spawned unit details and parent references preserve army totals", () =>
    {
        var c = new ContributionLedger(); var parent = new UnitKey(0, 1); var child = new UnitKey(0, 2);
        c.Spawn(10, child, parent, 0, "Production");
        c.Hit(10, 0, child, true, 90, 10, 1); c.Hit(10, 0, child, false, 40, 5);
        var native = DamageModel.Group(new[] { new DamageSample(child, 90, 1, 45, 5, 10) });
        var result = c.Enrich(native, 0);
        Equal(100L, result.TotalDealt); Equal(45L, result.TotalDamageTaken); Equal(1L, result.TotalKills);
        var detail = result.Rows.Single(r => r.Unit == parent).Contributions!.Single();
        Equal(true, detail.Child); Equal(90L, detail.Damage); Equal(40L, detail.Taken); Equal(10L, detail.Overkill);
        Equal(0L, result.Rows.Single(r => r.Unit == parent).Damage);
    }),
    ("Conversion preserves earlier army damage and credits later damage to Hacker's army", () =>
    {
        var c = new ContributionLedger(); var hacker = new UnitKey(0, 7); var unit = new UnitKey(0, 2);
        c.Hit(2, 1, unit, true, 40, 0); c.Hacks.Add(1, 2, 0, hacker, 100); c.Convert(2, unit, 0);
        c.Hit(2, 0, unit, true, 70, 5, 1); c.Hacks.Died(1); c.Hacks.Died(2);
        var oldSide = c.Enrich(DamageModel.Group(new[] { new DamageSample(unit, 40, 0) }), 1);
        var newSide = c.Enrich(DamageModel.Group(new[] { new DamageSample(unit, 70, 1, DealtOverkill: 5) }), 0);
        Equal(40L, oldSide.TotalDealt); Equal(75L, newSide.TotalDealt);
        Equal(70L, newSide.Rows.Single(r => r.Unit == hacker).Contributions!.Single().Damage);
        Equal(1L, newSide.Rows.Single(r => r.Unit == hacker).Hacking.Converted);
    }),
    ("Providerless owned buff damage is added once with its original tech", () =>
    {
        var c = new ContributionLedger(); var unit = new UnitKey(0, 4); var tech = new DamageSource(DamageCategory.Tech, 99, "Wave");
        c.Supplement(0, unit, tech, 90, 10, 1);
        var native = DamageModel.Group(new[] { new DamageSample(unit, 50, 0) });
        var first = c.Enrich(native, 0); var second = c.Enrich(native, 0);
        Equal(150L, first.TotalDealt); Equal(first.TotalDealt, second.TotalDealt);
        Equal(90L, first.Rows.Single().Sources(true).Single(s => s.Source == tech).Damage);
    }),
    ("Repeated conversion changes only subsequent child credit and cleanup is not another hack", () =>
    {
        var c = new ContributionLedger(); var hacker = new UnitKey(0, 7); var unit = new UnitKey(0, 2);
        c.Hacks.Add(1, 2, 0, hacker, 100); c.Convert(2, unit, 0); c.Hit(2, 0, unit, true, 30, 2);
        c.Hacks.Add(3, 2, 1, hacker, 80); c.Convert(2, unit, 1); c.Hit(2, 1, unit, true, 40, 3);
        c.Convert(2, unit, 0); // Battle cleanup restores original ownership.
        var empty = DamageModel.Group(Array.Empty<DamageSample>());
        var left = c.Enrich(empty, 0).Rows.Single(r => r.Unit == hacker);
        var right = c.Enrich(empty, 1).Rows.Single(r => r.Unit == hacker);
        Equal(1L, left.Hacking.Converted); Equal(1L, right.Hacking.Converted);
        Equal(30L, left.Contributions!.Single().Damage); Equal(40L, right.Contributions!.Single().Damage);
    }),
    ("Deployed and spawned units of the same type remain distinct within one unit row", () =>
    {
        var c = new ContributionLedger(); var parent = new UnitKey(0, 1); var unit = new UnitKey(0, 2);
        c.Hit(1, 0, unit, true, 40, 0);
        c.Spawn(2, unit, parent, 0, "Production"); c.Spawn(2, unit, parent, 0);
        c.Hit(2, 0, unit, true, 60, 10);
        var result = c.Enrich(DamageModel.Group(new[] { new DamageSample(unit, 100, 0, DealtOverkill: 10) }), 0);
        var details = result.Rows.Single(r => r.Unit == unit).Contributions!;
        Equal(2, details.Length); Equal(110L, result.TotalDealt);
        Equal(40L, details.Single(d => d.Origin == UnitOrigin.Deployed).Damage);
        Equal("Production", details.Single(d => d.Origin == UnitOrigin.Spawned).Tech);
        Equal(60L, result.Rows.Single(r => r.Unit == parent).Contributions!.Single().Damage);
    }),
    ("Round history retains hacking and child breakdowns without keeping mutable arrays", () =>
    {
        var history = new MatchHistory(); var unit = new UnitKey(0, 7);
        history.Begin(new MatchIdentity(1, 2, 3), "A", "B");
        var details = new[] { new Contribution(UnitOrigin.Hacked, new UnitKey(0, 8), unit, true, 70) };
        var stats = DamageModel.Group(new[] { new DamageSample(unit, 0, 0, Hacking: new HackStats(100, 20, 0, 1), Contributions: details) });
        history.Put(1, stats, DamageModel.Group(Array.Empty<DamageSample>()), true);
        details[0] = details[0] with { Damage = 999 };
        Equal(70L, history.Overall().Left.Rows.Single().Contributions!.Single().Damage);
        Equal(new HackStats(100, 20, 0, 1), history.Overall().Left.Rows.Single().Hacking);
    }),
    ("Unit cost counts squads, upgrades and shared tech once with undo and redo", () =>
    {
        var ledger = new UnitCostLedger(); var unit = new UnitKey(0, 5);
        ledger.Record(0, 0, "1", unit, UnitCostKind.Purchase, 200);
        ledger.Record(0, 0, "2", unit, UnitCostKind.Purchase, 200);
        ledger.Record(0, 0, "3", unit, UnitCostKind.Upgrade, 100);
        ledger.Record(0, 0, "4", unit, UnitCostKind.Technology, 350);
        ledger.Record(0, 1, "4", unit, UnitCostKind.Technology, 999);
        Equal(850L, ledger.Snapshot(0)[unit].Total);
        ledger.Undo(0, "4"); Equal(500L, ledger.Snapshot(0)[unit].Total);
        ledger.Record(0, 0, "4", unit, UnitCostKind.Technology, 350);
        ledger.Record(0, 0, "4", unit, UnitCostKind.Technology, 350);
        Equal(850L, ledger.Snapshot(0)[unit].Total);
    }),
    ("Cost history preserves round snapshots and Overall does not sum repeated investment", () =>
    {
        var history = new UnitCostHistory(); var unit = new UnitKey(0, 5);
        history.Begin(new MatchIdentity(1, 2, 3));
        var costs = new Dictionary<UnitKey, UnitCost> { [unit] = new(200, 0, 300) };
        history.Put(1, costs, new()); costs[unit] = new(400, 100, 300);
        history.Put(2, costs, new());
        Equal(500L, history.Get(1, false)![unit].Total);
        Equal(800L, history.Get(0, false)![unit].Total);
        history.Resume(1); Equal(500L, history.Get(0, false)![unit].Total);
        history.Begin(new MatchIdentity(4, 5, 6)); Equal(true, history.Get(0, false) is null);
    }),
    ("Cost ledger drops discarded replay rounds and preserves zero-priced actions", () =>
    {
        var ledger = new UnitCostLedger(); var unit = new UnitKey(0, 5);
        ledger.Record(0, 0, "1", unit, UnitCostKind.Purchase, 200);
        ledger.Record(1, 0, "2", unit, UnitCostKind.Upgrade, 100);
        ledger.Record(2, 0, "3", unit, UnitCostKind.Technology, 400);
        ledger.ObserveRound(1); Equal(200L, ledger.Snapshot(0)[unit].Total);
        ledger.Record(1, 0, "4", unit, UnitCostKind.Technology, 0);
        Equal(200L, ledger.Snapshot(0)[unit].Total);
    }),
    ("Expanded damage columns separate both channels without double-counting overkill", () =>
    {
        var tech = new DamageSource(DamageCategory.Tech, 1, "Explosion");
        var second = new DamageSource(DamageCategory.Tech, 2, "Extra weapon");
        var spell = new DamageSource(DamageCategory.Spell, 3, "Strike");
        var row = new DamageRow(new UnitKey(0, 1), 130, 0, 100, 40, 50,
            new[] { new SourceDamage(tech, 100, 20), new SourceDamage(second, 30, 30) },
            new[] { new SourceDamage(tech, 40, 15), new SourceDamage(spell, 20, 25) });
        var dealt = DamageBreakdown.Rows(row, true);
        var taken = DamageBreakdown.Rows(row, false);
        Equal(row.Damage, dealt.Where(d => d.Category).Sum(d => d.Damage));
        Equal(row.DealtOverkill, dealt.Where(d => d.Category).Sum(d => d.Overkill));
        Equal(row.DamageTaken - row.Overkill, taken.Where(d => d.Category).Sum(d => d.Damage));
        Equal(row.Overkill, taken.Where(d => d.Category).Sum(d => d.Overkill));
        var explosion = dealt.Single(d => !d.Category && d.Source == tech);
        Equal(100L, explosion.Damage);
        Equal(20L, explosion.Overkill);
        var incomingExplosion = taken.Single(d => !d.Category && d.Source == tech);
        Equal(40L, incomingExplosion.Damage);
        Equal(15L, incomingExplosion.Overkill);
        Equal(false, dealt.Any(d => d.Source == spell));
        Equal(false, taken.Any(d => d.Source == second));
        Equal(3, dealt.Length); // category plus two effects, no extra overkill row
        Equal(4, taken.Length); // two categories, one effect each
    }),
    ("Expanded regular damage is all non-overkill damage, not just normal attacks", () =>
    {
        var row = new DamageRow(new UnitKey(0, 1), 100, 0, 300, 200, 50,
            new[] { new SourceDamage(DamageSource.Attack, 100, 50) },
            new[] { new SourceDamage(DamageSource.Attack, 100, 200) });
        var detail = DamageBreakdown.Rows(row, true).Single();
        Equal(true, detail.Category);
        Equal(100L, detail.Damage);
        Equal(50L, detail.Overkill);
        var taken = DamageBreakdown.Rows(row, false).Single();
        Equal(100L, taken.Damage);
        Equal(200L, taken.Overkill);
        Equal(0, DamageBreakdown.Rows(new DamageRow(new UnitKey(0, 2), 0, 0, 0), true).Length);
    }),
    ("One unit's tech cannot appear in another unit's dealt report", () =>
    {
        var store = new DamageSourceStore();
        var tech = new DamageSource(DamageCategory.Tech, 42, "Explosion");
        store.Add(1, 100, 0, true, tech, 100, 10);
        store.Add(1, 200, 0, true, DamageSource.Attack, 70, 0);
        store.Add(1, 200, 0, false, tech, 30, 5);
        var team = DamageModel.Group(new[] {
            new DamageSample(new UnitKey(0, 1), 100, 0, 0, 0, 10, store.Get(1, 100, 0, true)),
            new DamageSample(new UnitKey(0, 2), 70, 0, 35, 5, 0,
                store.Get(1, 200, 0, true), store.Get(1, 200, 0, false))
        });
        var owner = team.Rows.Single(r => r.Unit.Id == 1);
        var victim = team.Rows.Single(r => r.Unit.Id == 2);
        Equal(100L, DamageBreakdown.Rows(owner, true).Single(d => !d.Category && d.Source == tech).Damage);
        Equal(false, DamageBreakdown.Rows(victim, true).Any(d => d.Source.Category == DamageCategory.Tech));
        Equal(30L, DamageBreakdown.Rows(victim, false).Single(d => !d.Category && d.Source == tech).Damage);
        Equal(0, DamageBreakdown.Rows(owner, false).Length);
    }),
    ("Tech from a later round does not appear in earlier round breakdowns", () =>
    {
        var tech = new DamageSource(DamageCategory.Tech, 42, "Explosion");
        var unit = new UnitKey(0, 1);
        var history = NewHistory();
        var empty = DamageModel.Group(Array.Empty<DamageSample>());
        history.Put(1, DamageModel.Group(new[] { new DamageSample(unit, 100, 0,
            DealtSources: new[] { new SourceDamage(DamageSource.Attack, 100, 0) }) }), empty, true);
        history.Put(2, DamageModel.Group(new[] { new DamageSample(unit, 50, 0,
            DealtSources: new[] { new SourceDamage(tech, 50, 0) }) }), empty, true);
        Equal(false, DamageBreakdown.Rows(history.Find(1)!.Left.Rows.Single(), true).Any(d => d.Source.Category == DamageCategory.Tech));
        Equal(50L, DamageBreakdown.Rows(history.Overall().Left.Rows.Single(), true).Single(d => !d.Category && d.Source == tech).Damage);
    }),
    ("Technology execution fills missing provenance without relabeling known nested hits", () =>
    {
        var tech = new DamageSource(DamageCategory.Tech, 42, "Bonus damage");
        var spell = new DamageSource(DamageCategory.Spell, 42, "Strike");
        Equal(tech, DamageAttribution.FillUnknown(DamageSource.Unknown, tech));
        Equal(DamageSource.Unknown, DamageAttribution.FillUnknown(DamageSource.Unknown, null));
        Equal(DamageSource.Unknown, DamageAttribution.FillUnknown(DamageSource.Unknown, DamageSource.Unknown));
        Equal(DamageSource.Attack, DamageAttribution.FillUnknown(DamageSource.Attack, tech));
        Equal(spell, DamageAttribution.FillUnknown(spell, tech));
        Equal(tech, DamageAttribution.FillUnknown(DamageSource.Unknown, tech, 10, 10));
        Equal(DamageSource.Unknown, DamageAttribution.FillUnknown(DamageSource.Unknown, tech, 10, 20));
        Equal(DamageSource.Unknown, DamageAttribution.FillUnknown(DamageSource.Unknown, tech, 10, 0));
        var store = new DamageSourceStore();
        store.Add(1, 10, 0, false, tech, 30, 4, false);
        store.Add(1, 10, 0, false, spell, 50, 8, true);
        var sources = store.Get(1, 10, 0, false);
        Equal(2, sources.Length);
        Equal(false, sources.Single(s => s.Source == tech).SourceRight);
        Equal(true, sources.Single(s => s.Source == spell).SourceRight);
        Equal(30L, sources.Single(s => s.Source == tech).Damage);
        Equal(50L, sources.Single(s => s.Source == spell).Damage);
    }),
    ("Execution provenance overrides a weapon only for the scoped target", () =>
    {
        var execution = new DamageSource(DamageCategory.Tech, 18, "Execution");
        Equal(execution, DamageAttribution.Resolve(DamageSource.Attack, execution, 20, 20));
        Equal(DamageSource.Attack, DamageAttribution.Resolve(DamageSource.Attack, execution, 20, 21));
        Equal(DamageSource.Attack, DamageAttribution.Resolve(DamageSource.Attack, DamageSource.Unknown, 20, 20));
        Equal(DamageSource.Attack, DamageAttribution.Resolve(DamageSource.Attack, null, 0, 20));
        Equal(execution, DamageAttribution.Resolve(DamageSource.Unknown, execution, 20, 20));
    }),
    ("Multi-target drop contexts preserve technology ownership", () =>
    {
        var drop = new DamageSource(DamageCategory.Tech, 25, "Air drop");
        foreach (var target in new long[] { 1, 2, 3 })
            Equal(drop, DamageAttribution.Resolve(DamageSource.Unknown, drop, 0, target));
        Equal(DamageSource.Attack, DamageAttribution.Resolve(DamageSource.Attack, null, -1, 1));
    }),
    ("Identified other effects stay distinct from unresolved hits and missing history", () =>
    {
        var buff = new DamageSource(DamageCategory.Other, 90, "Corrosion");
        var rows = DamageSources.Reconcile(new[] {
            new SourceDamage(buff, 50, 0), new SourceDamage(DamageSource.Unknown, 20, 0)
        }, 100, 5);
        Equal(50L, rows.Single(r => r.Source == buff).Total);
        Equal(20L, rows.Single(r => r.Source == DamageSource.Unknown).Total);
        Equal(35L, rows.Single(r => r.Source == DamageSource.Unobserved).Total);
        Equal(105L, rows.Sum(r => r.Total));
    }),
    ("Sources reconcile effective damage and overkill without assigning missing hits to attacks", () =>
    {
        var tech = new DamageSource(DamageCategory.Tech, 17, "Explosion");
        var rows = DamageSources.Reconcile(new[] { new SourceDamage(tech, 30, 5) }, 100, 20);
        Equal(100L, rows.Sum(r => r.Damage));
        Equal(20L, rows.Sum(r => r.Overkill));
        Equal(70L, rows.Single(r => r.Source == DamageSource.Unobserved).Damage);
        Equal(15L, rows.Single(r => r.Source == DamageSource.Unobserved).Overkill);
        Equal(false, rows.Any(r => r.Source == DamageSource.Attack));
        var stale = DamageSources.Reconcile(rows, 10, 1);
        Equal(1, stale.Length);
        Equal(DamageSource.Mismatch, stale[0].Source);
        Equal(11L, stale[0].Total);
    }),
    ("Source storage isolates sides, recorders, rounds and match identity", () =>
    {
        var store = new DamageSourceStore();
        var identity = new MatchIdentity(1, 2, 3);
        store.Begin(identity);
        store.Add(10, 20, 0, true, DamageSource.Attack, 40, 5);
        store.Add(10, 20, 0, false, DamageSource.Attack, 30, 2);
        store.Add(10, 20, 1, true, DamageSource.Attack, 7, 0);
        store.Add(11, 20, 0, true, DamageSource.Attack, 50, 0);
        Equal(45L, store.Get(10, 20, 0, true).Single().Total);
        Equal(32L, store.Get(10, 20, 0, false).Single().Total);
        Equal(7L, store.Get(10, 20, 1, true).Single().Total);
        store.RetainRounds(new long[] { 10 });
        Equal(0, store.Get(11, 20, 0, true).Length);
        store.Begin(identity);
        Equal(45L, store.Get(10, 20, 0, true).Single().Total);
        store.Begin(new MatchIdentity(2, 3, 4));
        Equal(0, store.Get(10, 20, 0, true).Length);
    }),
    ("Grouping retains named source totals on both channels", () =>
    {
        var unit = new UnitKey(0, 1);
        var tech = new DamageSource(DamageCategory.Tech, 17, "Explosion");
        var spell = new DamageSource(DamageCategory.Spell, 17, "Strike");
        var stats = DamageModel.Group(new[] {
            new DamageSample(unit, 100, 0, 50, 10, 20,
                new[] { new SourceDamage(tech, 100, 20) }, new[] { new SourceDamage(spell, 40, 10) }),
            new DamageSample(unit, 30, 0, 25, 0, 0,
                new[] { new SourceDamage(DamageSource.Attack, 30, 0) })
        });
        var row = stats.Rows.Single();
        Equal(150L, row.Sources(true).Sum(s => s.Total));
        Equal(65L, row.Sources(false).Sum(s => s.Total));
        Equal(120L, row.Sources(true).Single(s => s.Source == tech).Total);
        Equal(40L, row.Sources(false).Single(s => s.Source == spell).Total);
        Equal(25L, row.Sources(false).Single(s => s.Source == DamageSource.Unobserved).Total);
    }),
    ("Unowned spells retain team credit without attributing damage to a unit", () =>
    {
        var store = new DamageSourceStore();
        var spell = new DamageSource(DamageCategory.Spell, 42, "Strike");
        store.Begin(new MatchIdentity(1, 2, 3));
        store.AddUnowned(10, 0, spell, 300, 50, 1);
        store.AddUnowned(10, 0, spell, 200, 0, 0);
        var source = store.Unowned(10, 0).Single();
        Equal(500L, source.Damage);
        Equal(50L, source.Overkill);
        Equal(1L, source.Kills);
        Equal(0, store.Unowned(10, 1).Count());
        Equal(0, store.Get(10, 0, 0, true).Length);
        store.RetainRounds(Array.Empty<long>());
        Equal(0, store.Unowned(10, 0).Count());
    }),
    ("Source snapshots survive mutation, round replacement and overall aggregation", () =>
    {
        var history = NewHistory();
        var values = new[] { new SourceDamage(DamageSource.Attack, 100, 10) };
        var row = new DamageRow(new UnitKey(0, 1), 100, 0, 0, 0, 10, values);
        var stats = new TeamStats(new[] { row }, 100, 0, 0);
        history.Put(1, stats, DamageModel.Group(Array.Empty<DamageSample>()), true);
        values[0] = new SourceDamage(DamageSource.Attack, 999, 999);
        Equal(110L, history.Find(1)!.Left.Rows.Single().Sources(true).Sum(s => s.Total));
        history.Put(2, history.Find(1)!.Left, DamageModel.Group(Array.Empty<DamageSample>()), true);
        Equal(220L, history.Overall().Left.Rows.Single().Sources(true).Sum(s => s.Total));
        history.Put(2, history.Find(1)!.Left, DamageModel.Group(Array.Empty<DamageSample>()), true);
        Equal(220L, history.Overall().Left.Rows.Single().Sources(true).Sum(s => s.Total));
    }),
    ("Outgoing overkill belongs to the attacker and incoming overkill to the victim", () =>
    {
        var store = new OverkillStore();
        var hit = new OverkillHit(1);
        hit.Observe(20, 100, 1000, 100);
        var excess = hit.Consume(20, 1000);
        store.Add(1, 20, 1, excess);
        store.Add(1, 10, 0, excess, dealt: true);
        Equal(900L, store.Get(1, 10, 0, dealt: true));
        Equal(0L, store.Get(1, 10, 0));
        Equal(900L, store.Get(1, 20, 1));
        Equal(0L, store.Get(1, 20, 1, dealt: true));
        // Self-damage and team switching cannot collide with the other channel.
        store.Add(1, 10, 0, 40);
        store.Add(1, 10, 1, 50, dealt: true);
        Equal(900L, store.Get(1, 10, 0, dealt: true));
        Equal(40L, store.Get(1, 10, 0));
        store.RetainRounds(Array.Empty<long>());
        Equal(0L, store.Get(1, 10, 0, dealt: true));
        Equal(0L, store.Get(1, 20, 1));
    }),
    ("Dealt bars partition each army total including purple overkill", () =>
    {
        var left = DamageModel.Group(new[] {
            new DamageSample(new UnitKey(0, 1), 500, 1, 0, 0, 10),
            new DamageSample(new UnitKey(0, 2), 100, 1, 0, 0, 900)
        });
        var right = Team(800, 2, 0);
        Equal(600L, left.TotalDamage);
        Equal(1510L, left.TotalDealt);
        Equal(800L, right.TotalDealt);
        Equal(1, left.Rows[0].Unit.Id); // still ranked by effective damage
        var second = left.Rows[1];
        Equal(1000L, second.TotalDealt);
        Equal((float)(100d / 1510), DamageModel.BarFraction(second.Damage, left.TotalDealt));
        Equal((float)(900d / 1510), DamageModel.BarFraction(second.DealtOverkill, left.TotalDealt));
        var shares = left.Rows.Sum(row => DamageModel.BarFraction(row.TotalDealt, left.TotalDealt));
        Equal(true, Math.Abs(shares - 1) < 0.00001);
        Equal(true, DamageModel.BarFraction(second.TotalDealt, left.TotalDealt) < 1);
        Equal(0L, second.Overkill);
    }),
    ("Outgoing overkill survives grouping, round replacement and cumulative graphs", () =>
    {
        var key = new UnitKey(0, 1);
        var history = NewHistory();
        var first = DamageModel.Group(new[] {
            new DamageSample(key, 100, 1, 50, 25, 900),
            new DamageSample(key, 200, 2, 100, 40, 100)
        });
        var second = DamageModel.Group(new[] { new DamageSample(key, 500, 3, 0, 0, 2000) });
        var empty = DamageModel.Group(Array.Empty<DamageSample>());
        history.Put(1, first, empty, false);
        history.Put(1, first, empty, true);
        history.Put(2, second, empty, true);
        Equal(800L, history.Overall().Left.TotalDamage);
        Equal(3800L, history.Overall().Left.TotalDealt);
        Equal(3000L, history.Overall().Left.Rows[0].DealtOverkill);
        Equal(65L, history.Overall().Left.Rows[0].Overkill);
        Equal(3800L, history.Series(ResultMetric.Damage, true)[1].Left);
        Equal(3000L, history.OverkillSeries(true, dealt: true)[1].Left);
        Equal(2000L, history.OverkillSeries(false, dealt: true)[1].Left);
        history.Resume(1);
        Equal(1300L, history.Overall().Left.TotalDealt);
        Equal(1000L, history.Overall().Left.Rows[0].DealtOverkill);
    }),
    ("Nested death effects and multi-target hits consume only their own overkill", () =>
    {
        var outer = new OverkillHit(1);
        var explosion = new OverkillHit(1);
        // The inner effect completes while the outer health-loss call is pending.
        explosion.Observe(20, 50, 300, 50);
        explosion.Observe(30, 400, 100, 100);
        Equal(250L, explosion.Consume(20, 300));
        Equal(0L, explosion.Consume(30, 100));
        Equal(0L, outer.Consume(20, 300));
        outer.Observe(10, 100, 1000, 100);
        Equal(900L, outer.Consume(10, 1000));
        Equal(0L, outer.Consume(10, 1000)); // no duplicate from later notifications
    }),
    ("Observed excess is bounded by its taken event and cannot leak to another target", () =>
    {
        var hit = new OverkillHit(1);
        hit.Observe(10, 100, 1000, 100);
        Equal(0L, hit.Consume(11, 1000));
        Equal(500L, hit.Consume(10, 500));
        Equal(0L, hit.Consume(10, 1000));
        hit.Observe(10, 100, 1000, 100);
        Equal(0L, hit.Consume(10, -1));
        Equal(0L, hit.Consume(10, 1000));
    }),
    ("Overkill is lethal excess at the HP clamp, not mitigation or immunity", () =>
    {
        Equal(900L, OverkillStore.LethalExcess(100, 1000, 100));
        Equal(0L, OverkillStore.LethalExcess(100, 100, 100));
        Equal(0L, OverkillStore.LethalExcess(1000, 900, 900));
        // 1000 incoming reduced to 80 by armor: no overkill.
        Equal(0L, OverkillStore.LethalExcess(100, 80, 80));
        Equal(100L, OverkillStore.LethalExcess(100, 200, 100));
        Equal(0L, OverkillStore.LethalExcess(100, 1000, 0));
        Equal(0L, OverkillStore.LethalExcess(0, 1000, 0));
        Equal(0L, OverkillStore.LethalExcess(100, -5, 0));
    }),
    ("Overkill records keep rounds, recorders and controlled-unit teams separate", () =>
    {
        var store = new OverkillStore();
        store.Begin(new MatchIdentity(1, 2, 3));
        store.Add(10, 20, 0, 900);
        store.Add(10, 20, 0, 100);
        store.Add(10, 20, 1, 50);
        store.Add(11, 20, 0, 75);
        store.Add(10, 21, 0, 25);
        store.Begin(new MatchIdentity(1, 2, 3));
        Equal(1000L, store.Get(10, 20, 0));
        Equal(50L, store.Get(10, 20, 1));
        Equal(75L, store.Get(11, 20, 0));
        Equal(25L, store.Get(10, 21, 0));
        Equal(0L, store.Get(10, 99, 0));
        store.Begin(new MatchIdentity(1, 2, 4));
        Equal(0L, store.Get(10, 20, 0));
    }),
    ("Retry clears current and future overkill while retaining completed rounds", () =>
    {
        var store = new OverkillStore();
        store.Add(10, 20, 0, 100);
        store.Add(11, 20, 0, 200);
        store.Add(12, 20, 0, 300);
        store.RetainRounds(new[] { 10L });
        Equal(100L, store.Get(10, 20, 0));
        Equal(0L, store.Get(11, 20, 0));
        Equal(0L, store.Get(12, 20, 0));
        store.Add(11, 20, 0, 50);
        Equal(50L, store.Get(11, 20, 0));
    }),
    ("Yellow segments stay within taken totals and preserve them when grouped", () =>
    {
        var key = new UnitKey(0, 1);
        var stats = DamageModel.Group(new[] {
            new DamageSample(key, 10, 1, 1000, 900),
            new DamageSample(key, 20, 2, 500, 200),
            new DamageSample(new UnitKey(0, 2), 0, 0, 50, 100),
            new DamageSample(new UnitKey(0, 3), 0, 0, 100, -5)
        });
        Equal(1650L, stats.TotalDamageTaken);
        Equal(1100L, stats.Rows[0].Overkill);
        Equal(1500L, stats.Rows[0].DamageTaken);
        Equal(50L, stats.Rows.Single(r => r.Unit.Id == 2).Overkill);
        Equal(0L, stats.Rows.Single(r => r.Unit.Id == 3).Overkill);
        var fraction = DamageModel.BarFraction(400, 1500) + DamageModel.BarFraction(1100, 1500);
        Equal(true, Math.Abs(fraction - 1) < 0.00001);
    }),
    ("History preserves overkill across samples, totals and cumulative graphs", () =>
    {
        var key = new UnitKey(0, 1);
        var history = NewHistory();
        var first = DamageModel.Group(new[] { new DamageSample(key, 10, 1, 1000, 900) });
        var second = DamageModel.Group(new[] { new DamageSample(key, 20, 2, 500, 200) });
        var empty = DamageModel.Group(Array.Empty<DamageSample>());
        history.Put(1, first, empty, false);
        history.Put(1, first, empty, true);
        history.Put(2, second, empty, true);
        Equal(1100L, history.Overall().Left.Rows[0].Overkill);
        Equal(1500L, history.Overall().Left.TotalDamageTaken);
        Equal(200L, history.OverkillSeries(false)[1].Left);
        Equal(1100L, history.OverkillSeries(true)[1].Left);
        history.Resume(1);
        Equal(900L, history.Overall().Left.Rows[0].Overkill);
    }),
    ("Repeated live samples replace a round and final snapshots cannot be downgraded", () =>
    {
        var history = NewHistory();
        history.Put(1, Team(10, 2, 30), Team(30, 1, 10), false);
        history.Put(1, Team(100, 5, 300), Team(300, 4, 100), false);
        Equal(100L, history.Overall().Left.TotalDamage);
        history.Put(1, Team(120, 6, 320), Team(320, 5, 120), true);
        history.Put(1, Team(1, 0, 1), Team(1, 0, 1), false);
        Equal(120L, history.Overall().Left.TotalDamage);
        Equal(false, history.HasPartialRound);
        Equal(1, history.Count);
    }),
    ("Overall combines each completed round once and graphs follow selected metric", () =>
    {
        var history = NewHistory();
        history.Put(1, Team(100, 2, 400), Team(400, 3, 100), true);
        history.Put(2, Team(200, 5, 600), Team(600, 7, 200), true);
        history.Put(2, Team(200, 5, 600), Team(600, 7, 200), true);
        var total = history.Overall();
        Equal(300L, total.Left.TotalDamage);
        Equal(1000L, total.Left.TotalDamageTaken);
        Equal(7L, total.Left.TotalKills);
        Equal(1000L, total.Right.TotalDamage);
        Equal(1, total.Left.Rows.Length);
        Equal(600L, history.Series(ResultMetric.Tanked, false)[1].Left);
        Equal(1000L, history.Series(ResultMetric.Tanked, true)[1].Left);
        Equal(10L, history.Series(ResultMetric.Kills, true)[1].Right);
        Equal(2, history.Series(ResultMetric.Damage, false)[1].Round);
    }),
    ("Rewind removes future rounds and permits rerecording", () =>
    {
        var history = NewHistory();
        history.SyncArchiveCount(3);
        for (var i = 1; i <= 3; i++) history.Put(i, Team(i * 100, i, i), Team(0, 0, 0), true);
        history.Put(4, Team(400, 4, 4), Team(0, 0, 0), false);
        history.SyncArchiveCount(1);
        Equal(1, history.Count);
        history.Put(2, Team(25, 1, 5), Team(0, 0, 0), false);
        Equal(125L, history.Overall().Left.TotalDamage);
        Equal(true, history.HasPartialRound);
    }),
    ("Retry with unchanged archive size discards previous fight attempt", () =>
    {
        var history = NewHistory();
        history.SyncArchiveCount(0);
        history.Put(1, Team(900, 9, 90), Team(0, 0, 0), true);
        history.End(true, "Left won");
        history.Resume(0);
        Equal(0, history.Count);
        Equal(false, history.Ended);
        Equal(false, history.Finished);
        Equal<string?>(null, history.Outcome);
        history.Put(1, Team(15, 1, 7), Team(0, 0, 0), false);
        Equal(15L, history.Overall().Left.TotalDamage);
    }),
    ("Result survives native cleanup but a new match resets it", () =>
    {
        var history = NewHistory();
        history.SyncArchiveCount(1);
        history.Put(1, Team(100, 2, 300), Team(300, 4, 100), true);
        history.End(true, "Left won");
        history.End(false);
        Equal(false, history.SyncArchiveCount(0));
        Equal(100L, history.Overall().Left.TotalDamage);
        Equal(true, history.Finished);
        var session = history.Session;
        history.Begin(new MatchIdentity(11, 21, 31), "New left", "New right");
        Equal(session + 1, history.Session);
        Equal(0, history.Count);
        Equal(false, history.Ended);
        Equal("New left", history.LeftName);
    }),
    ("Disconnect retains a clearly incomplete last sample", () =>
    {
        var history = NewHistory();
        history.Put(1, Team(50, 1, 70), Team(70, 2, 50), false);
        history.End(false);
        Equal(true, history.Ended);
        Equal(false, history.Finished);
        Equal(true, history.HasPartialRound);
        Equal(50L, history.Overall().Left.TotalDamage);
    }),
    ("Archive backfill accepts missing rounds and keeps chronological graph order", () =>
    {
        var history = NewHistory();
        history.SyncArchiveCount(3);
        history.Put(3, Team(30, 3, 10), Team(10, 1, 30), true);
        Equal(true, history.NeedsArchiveRound(1, 3));
        history.Put(1, Team(10, 1, 20), Team(20, 2, 10), true);
        history.Put(2, Team(20, 2, 30), Team(30, 3, 20), true);
        Equal(false, history.NeedsArchiveRound(1, 3));
        Equal(true, history.NeedsArchiveRound(3, 3));
        Equal("1,2,3", string.Join(",", history.Series(ResultMetric.Damage, false).Select(p => p.Round)));
        Equal(60L, history.Overall().Left.TotalDamage);
    }),
    ("Saved records own their arrays and empty results remain valid", () =>
    {
        var history = NewHistory();
        Equal(0L, history.Overall().Left.TotalDamage);
        Equal(0, history.Series(ResultMetric.Damage, true).Length);
        var source = Team(100, 2, 30);
        history.Put(1, source, Team(0, 0, 0), true);
        source.Rows[0] = new DamageRow(new UnitKey(0, 1), 999, 999, 999);
        Equal(100L, history.Find(1)!.Left.Rows[0].Damage);
    }),
    ("Damage taken aggregates across squads without changing dealt-damage ranking", () =>
    {
        var left = DamageModel.Group(new[] { Sample(1, 500, 2, 40), Sample(2, 0, 0, 900), Sample(2, 0, 0, 600) });
        var right = DamageModel.Group(new[] { Sample(2, 700, 3, 1000) });
        Equal(1, left.Rows[0].Unit.Id);
        Equal(1500L, left.Rows[1].DamageTaken);
        Equal(1540L, left.TotalDamageTaken);
        Equal(1000L, right.TotalDamageTaken);
        Equal(1540L, left.TotalDamageTaken);
        Equal(1000L, right.TotalDamageTaken);
        Equal(500L, left.TotalDealt);
        Equal(700L, right.TotalDealt);
        Equal((float)(1500d / 1540), DamageModel.BarFraction(left.Rows[1].DamageTaken, left.TotalDamageTaken));
    }),
    ("Damage taken supports large totals, empty rounds and rewind snapshots", () =>
    {
        var full = DamageModel.Group(new[] { Sample(1, 0, 0, int.MaxValue), Sample(1, 0, 0, int.MaxValue) });
        Equal(4294967294L, full.TotalDamageTaken);
        var rewound = DamageModel.Group(new[] { Sample(1, 0, 0, 25), Sample(2, 0, 0, -1) });
        Equal(25L, rewound.TotalDamageTaken);
        Equal(0L, rewound.Rows.Single(row => row.Unit.Id == 2).DamageTaken);
        var empty = DamageModel.Group(Array.Empty<DamageSample>());
        Equal(0L, empty.TotalDamageTaken);
        Equal(0f, DamageModel.BarFraction(0, empty.TotalDamageTaken));
        Equal(1f, DamageModel.BarFraction(25, rewound.TotalDamageTaken));
    }),
    ("Combines squads of the same unit type", () =>
    {
        var result = DamageModel.Group(new[] { Sample(1, 100, 2), Sample(2, 200, 3), Sample(1, 400, 4) });
        Equal(2, result.Rows.Length);
        Equal(1, result.Rows[0].Unit.Id);
        Equal(500L, result.Rows[0].Damage);
        Equal(6L, result.Rows[0].Kills);
        Equal(700L, result.TotalDamage);
        Equal(9L, result.TotalKills);
    }),
    ("Opposing armies stay separate", () =>
    {
        var left = DamageModel.Group(new[] { Sample(1, 50, 2) });
        var right = DamageModel.Group(new[] { Sample(1, 200, 3) });
        Equal(50L, left.TotalDamage);
        Equal(200L, right.TotalDamage);
        Equal(50L, left.TotalDealt);
        Equal(200L, right.TotalDealt);
        Equal(1f, DamageModel.BarFraction(left.Rows[0].Damage, left.TotalDealt));
        Equal(1f, DamageModel.BarFraction(right.Rows[0].Damage, right.TotalDealt));
    }),
    ("Aggregates beyond the 32-bit damage limit", () =>
    {
        var result = DamageModel.Group(new[] { Sample(1, int.MaxValue, int.MaxValue), Sample(1, int.MaxValue, 1) });
        Equal(4294967294L, result.TotalDamage);
        Equal(2147483648L, result.TotalKills);
    }),
    ("Repeated snapshots and replay rewinds do not accumulate damage", () =>
    {
        var input = new[] { Sample(1, 1000, 30), Sample(2, 700, 8) };
        Equal(DamageModel.Group(input).TotalDamage, DamageModel.Group(input).TotalDamage);
        var rewound = DamageModel.Group(new[] { Sample(1, 100, 1) });
        Equal(100L, rewound.TotalDamage);
        Equal(1, rewound.Rows.Length);
    }),
    ("Equal damage sorts consistently by kills and unit ID", () =>
    {
        var result = DamageModel.Group(new[] { Sample(5, 20, 1), Sample(3, 20, 1), Sample(9, 20, 4) });
        Equal("9,3,5", string.Join(",", result.Rows.Select(row => row.Unit.Id)));
    }),
    ("Empty first round has finite zero bars", () =>
    {
        var empty = DamageModel.Group(Array.Empty<DamageSample>());
        Equal(0, empty.Rows.Length);
        Equal(0L, empty.TotalDamage);
        Equal(0L, empty.TotalDealt);
        Equal(0f, DamageModel.BarFraction(0, 0));
    }),
    ("Zero-damage units with kills remain visible", () =>
    {
        var result = DamageModel.Group(new[] { Sample(1, 0, 3), Sample(2, 0, 0) });
        Equal(2, result.Rows.Length);
        Equal(3L, result.TotalKills);
    }),
    ("Different recorder categories cannot collide", () =>
    {
        var result = DamageModel.Group(new[] { Sample(1, 10, 1), new DamageSample(new UnitKey(1, 1), 90, 9) });
        Equal(2, result.Rows.Length);
        Equal(100L, result.TotalDamage);
    }),
    ("Invalid negative values cannot produce negative bars", () =>
    {
        var result = DamageModel.Group(new[] { Sample(1, -5, -1) });
        Equal(0L, result.TotalDamage);
        Equal(0L, result.TotalKills);
        Equal(0f, DamageModel.BarFraction(-5, 100));
        Equal(1f, DamageModel.BarFraction(101, 100));
    }),
};

if (args.Length == 2 && args[0] == "--check-game")
{
    var compatibility = GameCompatibility.CheckInstalled(args[1]);
    Console.WriteLine(compatibility.Message);
    return compatibility.Compatible ? 0 : 1;
}

var allChecks = checks.Concat(ArchiveChecks.All).Concat(CompatibilityChecks.All).Concat(SquadChecks.All).Concat(PresentationChecks.All).Concat(TableChecks.All).Concat(HackingChecks.All).Concat(UnitLevelChecks.All).ToArray();
var failed = 0;
foreach (var (name, run) in allChecks)
{
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.WriteLine($"FAIL {name}: {error.Message}"); }
}
Console.WriteLine($"{allChecks.Length - failed}/{allChecks.Length} checks passed.");
return failed == 0 ? 0 : 1;

static DamageSample Sample(int id, long damage, long kills, long taken = 0) => new(new UnitKey(0, id), damage, kills, taken);
static TeamStats Team(long damage, long kills, long taken) => DamageModel.Group(new[] { Sample(1, damage, kills, taken) });
static MatchHistory NewHistory()
{
    var history = new MatchHistory();
    history.Begin(new MatchIdentity(10, 20, 30), "Left", "Right");
    return history;
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}, got {actual}.");
}
