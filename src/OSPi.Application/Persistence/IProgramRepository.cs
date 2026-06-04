namespace OSPi.Application.Persistence;

/// <summary>
/// CRUD for watering programs. <see cref="GetWithDetailsAsync"/> loads the full graph
/// (start times + per-zone durations); <see cref="GetAllAsync"/> is for list views.
/// </summary>
public interface IProgramRepository
{
    Task<IReadOnlyList<Domain.Entities.Program>> GetAllAsync(CancellationToken ct = default);

    Task<Domain.Entities.Program?> GetWithDetailsAsync(int id, CancellationToken ct = default);

    Task<int> AddAsync(Domain.Entities.Program program, CancellationToken ct = default);

    Task UpdateAsync(Domain.Entities.Program program, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}
