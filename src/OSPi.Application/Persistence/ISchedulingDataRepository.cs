namespace OSPi.Application.Persistence;

/// <summary>
/// Loads the full schedulable world in one read for the Phase 2 scheduler, avoiding
/// piecemeal queries during a scheduling tick.
/// </summary>
public interface ISchedulingDataRepository
{
    Task<SchedulingData> LoadAllAsync(CancellationToken ct = default);
}
