using Microsoft.EntityFrameworkCore;
using OSPi.Application.Persistence;
using OSPi.Domain.Entities;

namespace OSPi.Infrastructure.Persistence.Repositories;

internal sealed class ZoneRepository : IZoneRepository
{
    private readonly IDbContextFactory<OSPiDbContext> _factory;

    public ZoneRepository(IDbContextFactory<OSPiDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<Zone>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Zones.AsNoTracking().OrderBy(z => z.HardwareBit).ToListAsync(ct);
    }

    public async Task<Zone?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Zones.AsNoTracking().FirstOrDefaultAsync(z => z.Id == id, ct);
    }

    public async Task UpdateAsync(Zone zone, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Zones.Update(zone);
        await db.SaveChangesAsync(ct);
    }
}
