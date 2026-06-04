using OSPi.Domain.Entities;

namespace OSPi.Application.Persistence;

/// <summary>Reads and updates the two master stations (seeded rows).</summary>
public interface IMasterStationRepository
{
    Task<IReadOnlyList<MasterStation>> GetAllAsync(CancellationToken ct = default);

    Task UpdateAsync(MasterStation master, CancellationToken ct = default);
}
