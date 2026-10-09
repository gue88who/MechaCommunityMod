using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Explicit disk schema: do not serialize native identity pointers or rely on
// internal UI properties being included by reflection.
internal sealed record StatsDocument(string Format, int Version, string ModVersion,
    Guid Id, DateTimeOffset StartedUtc, DateTimeOffset SavedUtc,
    string LeftName, string RightName, bool Ended, bool Finished, string? Outcome,
    SavedRound[] Rounds, SavedRound Overall, SavedAvatar? LeftAvatar = null, SavedAvatar? RightAvatar = null,
    BoardSnapshot[]? Boards = null);
internal sealed record SavedRound(int Number, bool Complete, SavedTeam Left, SavedTeam Right, RoundInfo? Info = null);
internal sealed record SavedTeam(SavedUnit[] Units, SavedCost[] Costs);
internal sealed record SavedCost(UnitKey Unit, string Name, long Base, long Upgrades, long Technology);
internal sealed record SavedUnit(UnitKey Unit, string Name, long Damage, long Overkill,
    long Taken, long TakenOverkill, long Kills, long Casts, HackStats Hacking,
    SourceDamage[] DealtSources, SourceDamage[] TakenSources, SavedContribution[] Contributions,
    double XpEarned = 0, double EnemyXpAwarded = 0, SavedSquad[]? Squads = null, long? CoreDamage = null);
internal sealed record SavedSquad(string Id, string Name, long Damage, long Overkill, long Taken, long TakenOverkill,
    long Kills, double XpEarned, double EnemyXpAwarded, SourceDamage[] DealtSources, SourceDamage[] TakenSources,
    string? ParentId = null, UnitKey? ParentUnit = null, bool Spawned = false, long? Supply = null, long? Hacked = null, long? CoreDamage = null,
    HackStats? Hacking = null, UnitLevelSnapshot[]? Levels = null);
internal sealed record SavedContribution(UnitOrigin Origin, UnitKey Unit, string Name, UnitKey? Parent,
    string? ParentName, bool Child, string Tech, long Damage, long Overkill, long Taken, long TakenOverkill, long Kills);

internal static class StatsFormat
{
    internal const string Name = "mechabellum-stats";
    internal const int Version = 1;
    internal const string Extension = ".mechstats";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    internal static StatsDocument Capture(MatchHistory history, UnitCostHistory costs,
        Func<UnitKey, string> describe, string modVersion)
    {
        SavedTeam Team(TeamStats team, int round, bool right) => new(
            team.Rows.Select(r => new SavedUnit(r.Unit, describe(r.Unit), r.Damage, r.DealtOverkill,
                r.DamageTaken - r.Overkill, r.Overkill, r.Kills, r.Casts, r.Hacking,
                r.Sources(true), r.Sources(false), r.Contributions?.Select(c => new SavedContribution(
                    c.Origin, c.Unit, describe(c.Unit), c.Parent, c.Parent is { } parent ? describe(parent) : null,
                    c.Child, c.Tech, c.Damage, c.Overkill, c.Taken, c.TakenOverkill, c.Kills))
                    .ToArray() ?? Array.Empty<SavedContribution>(), r.XpEarned, r.EnemyXpAwarded,
                r.Squads?.Select(s => new SavedSquad(s.Id, s.Name, s.Damage, s.Overkill, s.Taken - s.TakenOverkill,
                    s.TakenOverkill, s.Kills, s.XpEarned, s.EnemyXpAwarded,
                    s.DealtSources?.ToArray() ?? Array.Empty<SourceDamage>(), s.TakenSources?.ToArray() ?? Array.Empty<SourceDamage>(),
                    s.ParentId, s.ParentUnit, s.Spawned, costs.GetSquads(round, right).TryGetValue(s.Id, out var investment)
                        ? investment.Purchase + investment.Upgrade : null, s.Hacked, s.CoreDamage, s.Hacking, s.Levels?.ToArray())).ToArray(), r.CoreDamage)).ToArray(),
            costs.Get(round, right)?.OrderBy(p => p.Key.Kind).ThenBy(p => p.Key.Id)
                .Select(p => new SavedCost(p.Key, describe(p.Key), p.Value.Purchase, p.Value.Upgrade, p.Value.Technology))
                .ToArray() ?? Array.Empty<SavedCost>());
        SavedRound Round(RoundRecord r) => new(r.Number, r.Complete,
            Team(r.Left, r.Number, false), Team(r.Right, r.Number, true), r.Info);
        return new(Name, Version, modVersion, history.ArchiveId, history.StartedUtc, DateTimeOffset.UtcNow,
            history.LeftName, history.RightName, history.Ended, history.Finished, history.Outcome,
            history.Rounds.Select(Round).ToArray(), Round(history.Overall()));
    }

    internal static string Serialize(StatsDocument document) => JsonSerializer.Serialize(document, Options);
    internal static StatsDocument Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<StatsDocument>(json, Options)
            ?? throw new InvalidDataException("Empty stats document.");
        if (document.Format != Name || document.Version != Version)
            throw new InvalidDataException("Unsupported stats format or version.");
        return document;
    }

    internal static string FileName(StatsDocument document) =>
        $"{document.StartedUtc:yyyyMMdd-HHmmss}_{document.Id:N}{Extension}";

    internal static string Write(string directory, StatsDocument document)
    {
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, FileName(document));
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, Options);
                stream.Flush(flushToDisk: true);
            }
            // Same-directory replacement keeps the previous checkpoint intact
            // if serialization or writing fails. Never edit a replay file.
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

internal sealed class StatsArchive
{
    private readonly string _directory;
    private readonly string _version;
    private Task _writes = Task.CompletedTask;
    private readonly ConcurrentQueue<(bool Error, string Message)> _messages = new();
    private (int Session, int Timeline, int Complete, bool Ended, bool Finished, string? Outcome)? _saved;
    private DateTimeOffset _retryAfter;

    internal StatsArchive(string directory, string version) => (_directory, _version) = (directory, version);
    internal Task PendingWrites => _writes;

    // Called on the main thread. Workers receive only detached managed records.
    internal void Observe(MatchHistory history, UnitCostHistory costs, Func<UnitKey, string> describe, bool force = false,
        Task<SavedAvatar?>? leftAvatar = null, Task<SavedAvatar?>? rightAvatar = null, BoardSnapshot[]? boards = null)
    {
        if ((history.Count == 0 && _saved?.Session != history.Session) || DateTimeOffset.UtcNow < _retryAfter) return;
        var completed = history.CompletedCount;
        if (!force && completed == 0 && !history.Ended
            && (_saved?.Session != history.Session || _saved?.Timeline == history.Timeline)) return;
        var stamp = (history.Session, history.Timeline, completed, history.Ended, history.Finished, history.Outcome);
        if (!force && _saved == stamp) return;
        var document = StatsFormat.Capture(history, costs, describe, _version) with { Boards = boards };
        _saved = stamp;
        _writes = _writes.ContinueWith(async _ =>
        {
            document = document with {
                LeftAvatar = leftAvatar is null ? null : await leftAvatar.ConfigureAwait(false),
                RightAvatar = rightAvatar is null ? null : await rightAvatar.ConfigureAwait(false)
            };
            try { _messages.Enqueue((false, "Stats saved: " + StatsFormat.Write(_directory, document))); }
            catch (Exception error) { _messages.Enqueue((true, "Stats could not be saved: " + error.Message)); }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
    }

    internal void DrainMessages(Action<bool, string> log)
    {
        while (_messages.TryDequeue(out var message))
        {
            if (message.Error)
            {
                _saved = null;
                _retryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
            }
            log(message.Error, message.Message);
        }
    }
}
