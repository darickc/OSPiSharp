using OSPi.Domain.Entities;

namespace OSPi.Application.Persistence;

/// <summary>
/// Reads and writes the single <see cref="PropertyMap"/> row and its zone markers.
/// <see cref="SaveMarkersAsync"/> merges the full desired marker set by zone (mirroring the
/// program/zone-duration sync), so the editor can hand over its complete list each save.
/// </summary>
public interface IPropertyMapRepository
{
    /// <summary>Loads the property map with its markers (the seeded row always exists).</summary>
    Task<PropertyMap> GetAsync(CancellationToken ct = default);

    /// <summary>Records the metadata of a freshly stored image on the single map row.</summary>
    Task UpdateImageAsync(string imagePath, string imageHash, int width, int height, CancellationToken ct = default);

    /// <summary>Replaces the map's markers with the supplied set, merging by <c>ZoneId</c>.</summary>
    Task SaveMarkersAsync(IReadOnlyList<MapMarker> markers, CancellationToken ct = default);
}
