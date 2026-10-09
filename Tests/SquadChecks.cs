using MechaCommunityMod.Plugins.BattleStatistics;

internal static class SquadChecks
{
    internal static readonly (string, Action)[] All = {
        ("Same-type squads remain distinct and add to one army total", Split),
        ("Squad history replaces polling samples and combines rounds once", History),
        ("Squad history copies source arrays and retains names", Copies),
        ("XP remains fractional, team-specific and resets on rewind", Experience),
        ("XP-only squads remain in results without inventing damage", XpOnly),
        ("Squads and type totals use the same spell-overkill policy", Spell),
        ("Squad names remain unique beyond the name list", Names),
        ("Saved squad XP and source details round trip without pointers", Archive),
        ("Old stats files without squad fields remain readable", OldArchive),
        ("Live squad splits preserve partial totals and aggregate hacking", Presentation)
    };
    private static readonly UnitKey Unit = new(0, 5);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static DamageSample Sample(string id, string name, long damage, double xp, double awarded = 0) =>
        new(Unit, damage, 1, Squads: new[] { new SquadStats(id, name, damage, 1, XpEarned: xp, EnemyXpAwarded: awarded) });
    private static void Split()
    {
        var team = DamageModel.Group(new[] { Sample("a", "Melter Gary", 100, 0.25), Sample("b", "Melter Bob", 200, 1.5) });
        Check(team.Rows.Length == 1 && team.TotalDamage == 300 && team.TotalKills == 2, "Squads changed army totals");
        var row = team.Rows.Single();
        Check(row.Squads!.Length == 2 && row.XpEarned == 1.75 && row.Squads.Sum(s => s.Damage) == row.Damage, "Squads merged by type");
    }
    private static MatchHistory NewHistory()
    {
        var h = new MatchHistory(); h.Begin(new(1, 2, 3), "Left", "Right"); return h;
    }
    private static void History()
    {
        var h = NewHistory(); var team = DamageModel.Group(new[] { Sample("a", "Fortress Bob", 100, 1.25, 2.5) });
        var empty = DamageModel.Group(Array.Empty<DamageSample>());
        h.Put(1, team, empty, false); h.Put(1, team, empty, true); h.Put(1, team, empty, false); h.Put(2, team, empty, true);
        var overall = h.Overall().Left.Rows.Single();
        Check(overall.Squads!.Length == 1 && overall.Damage == 200 && overall.XpEarned == 2.5 && overall.EnemyXpAwarded == 5, "Polling multiplied squad values");
        h.Resume(1); Check(h.Overall().Left.Rows.Single().XpEarned == 1.25, "Rewind kept future XP");
    }
    private static void Copies()
    {
        var sources = new[] { new SourceDamage(DamageSource.Attack, 50, 0) };
        var squads = new[] { new SquadStats("a", "Amanda's Crawler squad", 50, DealtSources: sources) };
        var h = NewHistory(); var row = new DamageRow(Unit, 50, 0, 0) { Squads = squads };
        h.Put(1, new(new[] { row }, 50, 0, 0), new(Array.Empty<DamageRow>(), 0, 0, 0), true);
        sources[0] = new(DamageSource.Unknown, 999, 0); squads[0] = new("other", "Wrong");
        var saved = h.Find(1)!.Left.Rows.Single().Squads!.Single();
        Check(saved.Name == "Amanda's Crawler squad" && saved.DealtSources!.Single().Damage == 50, "Native refresh mutated history");
    }
    private static void Experience()
    {
        var ledger = new ExperienceLedger();
        ledger.Add(1, 0, "a", 0.125, 0); ledger.Add(1, 0, "a", 0.375, 1.25);
        ledger.Add(1, 1, "a", 9, 2); ledger.Add(2, 0, "a", 3, 4);
        Check(ledger.Get(1, 0, "a") == (0.5, 1.25) && ledger.Get(1, 1, "a") == (9d, 2d), "XP lost precision or crossed teams");
        ledger.Add(1, 0, "a", double.NaN, -5); ledger.Add(1, 0, "a", double.PositiveInfinity, double.NegativeInfinity);
        Check(ledger.Get(1, 0, "a") == (0.5, 1.25), "Invalid XP was accepted");
        ledger.Retain(new[] { 1L }); Check(ledger.Get(2, 0, "a") == (0d, 0d), "Rewind retained XP");
        ledger.Clear(); Check(ledger.Get(1, 0, "a") == (0d, 0d), "Match reset retained XP");
    }
    private static void XpOnly()
    {
        var team = DamageModel.Group(new[] { new DamageSample(Unit, 0, 0, Squads: new[] { new SquadStats("assist", "Melter Gary", XpEarned: 2.75) }) });
        Check(team.Rows.Length == 1 && team.TotalDamage == 0 && team.TotalKills == 0 && team.Rows.Single().XpEarned == 2.75, "XP assists lost or invented combat values");
    }
    private static void Spell()
    {
        var source = new[] { new SourceDamage(new(DamageCategory.Spell, 10, "Strike"), 100, 900) };
        var team = DamageModel.Group(new[] { new DamageSample(Unit, 100, 1, 1000, 900, 900, source, source,
            Squads: new[] { new SquadStats("a", "Fortress Bob", 100, 1, 1000, 900, 900, DealtSources: source, TakenSources: source) }) });
        var row = team.Rows.Single(); var squad = row.Squads!.Single();
        Check(row.TotalDealt == 100 && row.DamageTaken == 100 && squad.Damage + squad.Overkill == 100
            && squad.Taken == 100 && squad.TakenOverkill == 0, "Squad overkill disagrees with type totals");
    }
    private static void Names()
    {
        Check(Enumerable.Range(0, 1000).Select(SquadNames.At).Distinct().Count() == 1000, "Name list repeats");
        Check(SquadNames.Label("Melter", "Gary", false) == "Melter Gary"
            && SquadNames.Label("Crawler", "Amanda", true) == "Amanda's Crawler squad", "Labels changed");
    }
    private static StatsDocument Document()
    {
        var h = NewHistory(); h.Put(1, DamageModel.Group(new[] { Sample("deployed:0:4", "Fortress Bob", 100, 2.5, 1.25) }),
            DamageModel.Group(Array.Empty<DamageSample>()), true);
        return StatsFormat.Capture(h, new(), _ => "Fortress", "test");
    }
    private static void Archive()
    {
        var json = StatsFormat.Serialize(Document()); var row = StatsFormat.Deserialize(json).Overall.Left.Units.Single();
        Check(row.XpEarned == 2.5 && row.EnemyXpAwarded == 1.25 && row.Squads!.Single().Name == "Fortress Bob", "Saved squad data lost");
        Check(row.Squads!.Single().DealtSources.Single().Source == DamageSource.Unobserved, "Unobserved source was invented");
    }
    private static void OldArchive()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(StatsFormat.Serialize(Document()))!;
        void Strip(System.Text.Json.Nodes.JsonNode? node)
        {
            if (node is System.Text.Json.Nodes.JsonObject obj)
            {
                obj.Remove("squads"); obj.Remove("xpEarned"); obj.Remove("enemyXpAwarded");
                foreach (var child in obj.ToArray()) Strip(child.Value);
            }
            else if (node is System.Text.Json.Nodes.JsonArray array) foreach (var child in array) Strip(child);
        }
        Strip(json); var row = StatsFormat.Deserialize(json.ToJsonString()).Overall.Left.Units.Single();
        Check(row.Squads is null && row.XpEarned == 0 && row.Damage == 100, "Older document rejected or changed");
    }
    private static void Presentation()
    {
        var row = new DamageRow(Unit, 100, 2, 40, 5, 20) { Hacking = new(80, 20, 0, 1),
            Squads = new[] { new SquadStats("a", "Gary", 60, 1, 20, 10, 2, 1.5, 2) } };
        var team = new TeamStats(new[] { row }, 100, 2, 40);
        var split = SquadPresentation.Split(team, true, _ => "Hacker");
        Check(split.Rows.Length == 3 && split.Rows.Sum(r => r.TotalDealt) == 120 && split.Rows.Sum(r => r.DamageTaken) == 40
            && split.Rows.Sum(r => r.Kills) == 2 && split.Rows.Sum(r => r.XpEarned) == 1.5, "Split lost or multiplied totals");
        Check(split.Rows.Single(r => r.Hacking.Total > 0).DisplayName == "Hacker hacking totals", "Type hacking was assigned to one squad");
        var hidden = SquadPresentation.Split(team, false); Check(hidden.Rows.Length == 2, "Hacking toggle ignored");
        var invalid = team with { Rows = new[] { row with { Squads = new[] { new SquadStats("a", "Gary", 200) } } } };
        Check(SquadPresentation.Split(invalid, false).Rows.Single().Damage == 100, "Inconsistent squad data inflated totals");
    }
}
