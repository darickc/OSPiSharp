using Microsoft.EntityFrameworkCore;
using OSPi.Application.Persistence;
using OSPi.Domain.Entities;

namespace OSPi.Infrastructure.Persistence.Repositories;

internal sealed class ControllerSettingsRepository : IControllerSettingsRepository
{
    private readonly IDbContextFactory<OSPiDbContext> _factory;

    public ControllerSettingsRepository(IDbContextFactory<OSPiDbContext> factory) => _factory = factory;

    public async Task<ControllerSettings> GetAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ControllerSettings.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("ControllerSettings row is missing (should be seeded).");
    }

    public async Task UpdateAsync(ControllerSettings settings, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.ControllerSettings.Update(settings);
        await db.SaveChangesAsync(ct);
    }
}
