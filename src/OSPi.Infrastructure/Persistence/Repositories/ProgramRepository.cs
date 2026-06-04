using Microsoft.EntityFrameworkCore;
using OSPi.Application.Persistence;
using Program = OSPi.Domain.Entities.Program;

namespace OSPi.Infrastructure.Persistence.Repositories;

internal sealed class ProgramRepository : IProgramRepository
{
    private readonly IDbContextFactory<OSPiDbContext> _factory;

    public ProgramRepository(IDbContextFactory<OSPiDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<Program>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // StartTimes are AutoInclude'd; ZoneDurations omitted for the lightweight list view.
        return await db.Programs.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
    }

    public async Task<Program?> GetWithDetailsAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Programs
            .AsNoTracking()
            .Include(p => p.ZoneDurations)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<int> AddAsync(Program program, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Programs.Add(program);
        await db.SaveChangesAsync(ct);
        return program.Id;
    }

    public async Task UpdateAsync(Program program, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var existing = await db.Programs
            .Include(p => p.ZoneDurations)
            .FirstOrDefaultAsync(p => p.Id == program.Id, ct)
            ?? throw new InvalidOperationException($"Program {program.Id} not found.");

        // Scalars (and the owned StartTimes collection, replaced below).
        db.Entry(existing).CurrentValues.SetValues(program);

        // Replace owned start times wholesale (EF deletes/inserts owned rows).
        existing.StartTimes.Clear();
        existing.StartTimes.AddRange(program.StartTimes);

        SyncZoneDurations(db, existing, program);

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Programs.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (existing is null)
        {
            return;
        }

        db.Programs.Remove(existing);
        await db.SaveChangesAsync(ct);
    }

    private static void SyncZoneDurations(OSPiDbContext db, Program existing, Program incoming)
    {
        // Remove durations no longer present.
        var incomingZoneIds = incoming.ZoneDurations.Select(d => d.ZoneId).ToHashSet();
        var toRemove = existing.ZoneDurations.Where(d => !incomingZoneIds.Contains(d.ZoneId)).ToList();
        db.ProgramZoneDurations.RemoveRange(toRemove);

        foreach (var d in incoming.ZoneDurations)
        {
            var match = existing.ZoneDurations.FirstOrDefault(e => e.ZoneId == d.ZoneId);
            if (match is null)
            {
                existing.ZoneDurations.Add(new Domain.Entities.ProgramZoneDuration
                {
                    ZoneId = d.ZoneId,
                    DurationSeconds = d.DurationSeconds,
                    RunOrder = d.RunOrder,
                });
            }
            else
            {
                match.DurationSeconds = d.DurationSeconds;
                match.RunOrder = d.RunOrder;
            }
        }
    }
}
