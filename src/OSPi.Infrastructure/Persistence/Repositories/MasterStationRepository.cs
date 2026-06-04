using Microsoft.EntityFrameworkCore;
using OSPi.Application.Persistence;
using OSPi.Domain.Entities;

namespace OSPi.Infrastructure.Persistence.Repositories;

internal sealed class MasterStationRepository : IMasterStationRepository
{
    private readonly IDbContextFactory<OSPiDbContext> _factory;

    public MasterStationRepository(IDbContextFactory<OSPiDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<MasterStation>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MasterStations.AsNoTracking().OrderBy(m => m.MasterIndex).ToListAsync(ct);
    }

    public async Task UpdateAsync(MasterStation master, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.MasterStations.Update(master);
        await db.SaveChangesAsync(ct);
    }
}
