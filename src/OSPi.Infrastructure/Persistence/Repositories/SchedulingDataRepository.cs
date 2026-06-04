using Microsoft.EntityFrameworkCore;
using OSPi.Application.Persistence;

namespace OSPi.Infrastructure.Persistence.Repositories;

internal sealed class SchedulingDataRepository : ISchedulingDataRepository
{
    private readonly IDbContextFactory<OSPiDbContext> _factory;

    public SchedulingDataRepository(IDbContextFactory<OSPiDbContext> factory) => _factory = factory;

    public async Task<SchedulingData> LoadAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Programs include their durations (StartTimes are AutoInclude'd).
        var programs = await db.Programs
            .AsNoTracking()
            .Include(p => p.ZoneDurations)
            .ToListAsync(ct);

        var zones = await db.Zones.AsNoTracking().OrderBy(z => z.HardwareBit).ToListAsync(ct);
        var masters = await db.MasterStations.AsNoTracking().OrderBy(m => m.MasterIndex).ToListAsync(ct);
        var settings = await db.ControllerSettings.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("ControllerSettings row is missing (should be seeded).");

        return new SchedulingData(programs, zones, masters, settings);
    }
}
