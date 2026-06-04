using Microsoft.EntityFrameworkCore;
using OSPi.Application.Persistence;
using OSPi.Domain.Entities;

namespace OSPi.Infrastructure.Persistence.Repositories;

internal sealed class PropertyMapRepository : IPropertyMapRepository
{
    private readonly IDbContextFactory<OSPiDbContext> _factory;
    private readonly ImageStorageOptions _storage;

    public PropertyMapRepository(IDbContextFactory<OSPiDbContext> factory, ImageStorageOptions storage)
    {
        _factory = factory;
        _storage = storage;
    }

    public async Task<PropertyMap> GetAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.PropertyMaps
                   .AsNoTracking()
                   .Include(m => m.Markers)
                   .FirstOrDefaultAsync(ct)
               ?? throw new InvalidOperationException("PropertyMap row is missing (should be seeded).");
    }

    public async Task UpdateImageAsync(string imagePath, string imageHash, int width, int height, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var map = await db.PropertyMaps.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("PropertyMap row is missing (should be seeded).");

        var oldPath = map.ImagePath;

        map.ImagePath = imagePath;
        map.ImageHash = imageHash;
        map.ImageWidth = width;
        map.ImageHeight = height;
        await db.SaveChangesAsync(ct);

        // Only after the row commits do we remove the superseded file (best-effort): a crash
        // mid-update must never leave the row pointing at a deleted file.
        if (!string.IsNullOrEmpty(oldPath) && !string.Equals(oldPath, imagePath, StringComparison.Ordinal))
        {
            TryDelete(oldPath);
        }
    }

    public async Task SaveMarkersAsync(IReadOnlyList<MapMarker> markers, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var map = await db.PropertyMaps
            .Include(m => m.Markers)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("PropertyMap row is missing (should be seeded).");

        // Merge by ZoneId (match/update/remove) rather than clear-and-re-add, so the unique
        // (PropertyMapId, ZoneId) index is never momentarily violated during SaveChanges.
        var incomingZoneIds = markers.Select(m => m.ZoneId).ToHashSet();
        var toRemove = map.Markers.Where(m => !incomingZoneIds.Contains(m.ZoneId)).ToList();
        db.MapMarkers.RemoveRange(toRemove);

        foreach (var incoming in markers)
        {
            var match = map.Markers.FirstOrDefault(m => m.ZoneId == incoming.ZoneId);
            if (match is null)
            {
                map.Markers.Add(new MapMarker
                {
                    ZoneId = incoming.ZoneId,
                    X = incoming.X,
                    Y = incoming.Y,
                });
            }
            else
            {
                match.X = incoming.X;
                match.Y = incoming.Y;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private void TryDelete(string relativePath)
    {
        try
        {
            var full = Path.Combine(_storage.ResolveDirectory(), relativePath);
            if (File.Exists(full))
            {
                File.Delete(full);
            }
        }
        catch (IOException)
        {
            // Orphaned file is harmless; never fail the save over cleanup.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
