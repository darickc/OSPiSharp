using OSPi.Domain.Entities;

namespace OSPi.Application.Persistence;

/// <summary>Reads and appends run-history entries. The engine writes via this in Phase 3.</summary>
public interface IRunLogRepository
{
    Task<IReadOnlyList<RunLogEntry>> GetRecentAsync(int take = 100, CancellationToken ct = default);

    Task AddAsync(RunLogEntry entry, CancellationToken ct = default);
}
