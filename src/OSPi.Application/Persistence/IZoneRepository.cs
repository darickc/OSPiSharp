using OSPi.Domain.Entities;

namespace OSPi.Application.Persistence;

/// <summary>
/// Reads and updates the 16 fixed zones. Zones are seeded, not created or deleted.
/// </summary>
public interface IZoneRepository
{
    Task<IReadOnlyList<Zone>> GetAllAsync(CancellationToken ct = default);

    Task<Zone?> GetAsync(int id, CancellationToken ct = default);

    Task UpdateAsync(Zone zone, CancellationToken ct = default);
}
