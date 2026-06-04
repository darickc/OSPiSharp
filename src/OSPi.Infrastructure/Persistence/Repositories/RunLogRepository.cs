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
        return await db.RunLog
            .AsNoTracking()
            .OrderByDescending(e => e.EndTime)
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
