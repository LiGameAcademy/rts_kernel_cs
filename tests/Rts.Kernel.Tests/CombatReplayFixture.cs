using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rts.Kernel;
using Rts.Kernel.Combat;
using Rts.Kernel.Numerics;

/// <summary>Test host only: owns entity inputs and future command admission, not combat rules.</summary>
internal sealed class CombatReplayFixture
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal StationaryCombat Combat { get; private set; }
    internal List<EntityState> Entities { get; } = [];
    private readonly List<ReplayCommand> _pending = [];
    private long _nextSequence;
    private long _nextArrival;

    internal CombatReplayFixture(ulong seed) => Combat = new(seed);

    internal void Add(ulong id, int owner, int x, CombatDefinition definition)
    {
        Combat.AddCombatant(new(id), definition);
        Entities.Add(new(new(id), owner, new(x, 0), FixedVector2.Zero));
    }

    internal void Queue(long frame, int player, CombatCommandKind kind, ulong attacker, ulong target = 0)
    {
        if (frame <= Combat.Frame || frame > long.MaxValue - CombatTiming.MaxFrames)
            throw new ArgumentOutOfRangeException(nameof(frame));
        _pending.Add(new(frame, _nextArrival++, player, _nextSequence++, kind, attacker, target));
    }

    internal CombatStepResult Step()
    {
        var frame = Combat.Frame + 1;
        var commands = _pending.Where(command => command.Frame == frame).OrderBy(command => command.Arrival)
            .Select(command => new CombatCommand(command.Player, command.Sequence, command.Kind, new(command.Attacker), new(command.Target))).ToArray();
        _pending.RemoveAll(command => command.Frame == frame);
        return Combat.Advance(frame, Entities, commands);
    }

    internal string Save()
    {
        var entities = Entities.OrderBy(entity => entity.Id).Select(entity =>
            new ReplayEntity(entity.Id.Value, entity.OwnerId, entity.Position.X.Raw, entity.Position.Y.Raw)).ToArray();
        return JsonSerializer.Serialize(new ReplaySave(1, CombatSnapshotJson.Serialize(Combat.CaptureSnapshot()), entities,
            _pending.OrderBy(command => command.Frame).ThenBy(command => command.Arrival).ToArray(), _nextSequence, _nextArrival), Options);
    }

    internal string Hash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Save())));

    internal static CombatReplayFixture Load(string json, IReadOnlyList<CombatDefinitionEntry> definitions)
    {
        var saved = JsonSerializer.Deserialize<ReplaySave>(json, Options) ?? throw new InvalidDataException("Missing host save.");
        if (saved.Version != 1 || saved.Entities is null || saved.Pending is null || saved.NextSequence < 0 || saved.NextArrival < 0)
            throw new InvalidDataException("Invalid host envelope.");
        var combat = StationaryCombat.Restore(CombatSnapshotJson.Deserialize(saved.Combat), definitions);
        var identities = new HashSet<ulong>();
        foreach (var entity in saved.Entities)
        {
            if (entity is null || entity.Id == 0 || !identities.Add(entity.Id) || entity.Owner < 0
                || !SimulationDomain.Contains(FixedVector2.FromRaw(entity.X, entity.Y)))
                throw new InvalidDataException("Invalid host entity input.");
        }
        var arrivals = new HashSet<long>();
        var sequences = new HashSet<long>();
        foreach (var command in saved.Pending)
        {
            if (command is null || command.Frame <= combat.Frame || command.Frame > long.MaxValue - CombatTiming.MaxFrames
                || command.Arrival < 0 || command.Arrival >= saved.NextArrival || !arrivals.Add(command.Arrival)
                || command.Sequence < 0 || command.Sequence >= saved.NextSequence || !sequences.Add(command.Sequence)
                || command.Player < 0 || command.Attacker == 0 || !Enum.IsDefined(command.Kind)
                || (command.Kind == CombatCommandKind.Stop ? command.Target != 0 : command.Target == 0))
                throw new InvalidDataException("Invalid host pending command.");
        }
        var host = new CombatReplayFixture(combat.RngState)
        {
            Combat = combat,
            _nextArrival = saved.NextArrival,
            _nextSequence = saved.NextSequence
        };
        host.Entities.AddRange(saved.Entities.Select(entity =>
            new EntityState(new(entity.Id), entity.Owner, FixedVector2.FromRaw(entity.X, entity.Y), FixedVector2.Zero)));
        host._pending.AddRange(saved.Pending);
        return host;
    }
}

internal sealed record ReplayEntity(
    [property: JsonRequired] ulong Id, [property: JsonRequired] int Owner,
    [property: JsonRequired] long X, [property: JsonRequired] long Y);
internal sealed record ReplayCommand(
    [property: JsonRequired] long Frame, [property: JsonRequired] long Arrival,
    [property: JsonRequired] int Player, [property: JsonRequired] long Sequence,
    [property: JsonRequired] CombatCommandKind Kind, [property: JsonRequired] ulong Attacker,
    [property: JsonRequired] ulong Target);
internal sealed record ReplaySave(
    [property: JsonRequired] int Version, [property: JsonRequired] string Combat,
    [property: JsonRequired] ReplayEntity[] Entities, [property: JsonRequired] ReplayCommand[] Pending,
    [property: JsonRequired] long NextSequence, [property: JsonRequired] long NextArrival);
