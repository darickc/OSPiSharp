using System.Collections.Immutable;

namespace OSPi.Application.Engine;

/// <summary>Immutable per-second projection of engine state, pushed to the UI.</summary>
public sealed record StatusSnapshot
{
    public required DateTimeOffset TimestampUtc { get; init; }
    public required ImmutableArray<ZoneStatus> Zones { get; init; }
    public bool SystemEnabled { get; init; } = true;
}

public sealed record ZoneStatus
{
    public required int ZoneId { get; init; }
    public required bool On { get; init; }
    /// <summary>Seconds remaining for a timed run, or null for indefinite/manual/off.</summary>
    public int? SecondsRemaining { get; init; }
}
