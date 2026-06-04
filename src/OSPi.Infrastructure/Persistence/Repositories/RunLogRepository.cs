using Microsoft.EntityFrameworkCore;
using OSPi.Application.Persistence;
using OSPi.Domain.Entities;

namespace OSPi.Infrastructure.Persistence.Repositories;

internal sealed class RunLogRepository : IRunLogRepository
{
    private readonly IDbContextFactory<OSPiDbContext> _factory;

    public RunLogRepository(IDbContextFactory<OSPiDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<RunLogEntry>> GetRecentAsync(int take = 100, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // SQLite cannot ORDER BY a DateTimeOffset (stored as TEXT). Id is an identity assigned at
        // insert time, so for an append-only log it is monotonic with completion order — ordering
        // by Id descending gives "most recent first" without a schema change.
        return await db.RunLog
            .AsNoTracking()
            .Include(e => e.Zone)
            .Include(e => e.Program)
            .OrderByDescending(e => e.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task AddAsync(RunLogEntry entry, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.RunLog.Add(entry);
        await db.SaveChangesAsync(ct);
    }
}
