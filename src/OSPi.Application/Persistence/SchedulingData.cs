using OSPi.Domain.Entities;

namespace OSPi.Application.Persistence;

/// <summary>
/// A single snapshot of everything the scheduler needs: all programs (with their start
/// times and per-zone durations), all zones, both master stations, and controller
/// settings. Loaded in one call by <see cref="ISchedulingDataRepository"/> in Phase 2.
/// </summary>
public sealed record SchedulingData(
    IReadOnlyList<Domain.Entities.Program> Programs,
    IReadOnlyList<Zone> Zones,
    IReadOnlyList<MasterStation> Masters,
    ControllerSettings Settings);
