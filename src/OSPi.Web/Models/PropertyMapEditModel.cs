using OSPi.Domain.Entities;

namespace OSPi.Web.Models;

/// <summary>
/// Editable view model for the Property Map editor. Keeps the domain <see cref="PropertyMap"/>
/// POCO free of UI concerns. Markers are placed/moved by zone with <see cref="Place"/> (the
/// pure, unit-tested core that clamps to 0..1); map in via <see cref="FromEntity"/> and out via
/// <see cref="ToEntity"/>. At most one marker per zone, enforced by <see cref="Place"/>.
/// </summary>
public sealed class PropertyMapEditModel
{
    public string? ImagePath { get; set; }
    public string? ImageHash { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }

    public List<MarkerRow> Markers { get; set; } = new();

    /// <summary>True once an image has been uploaded and stored.</summary>
    public bool HasImage => !string.IsNullOrEmpty(ImageHash);

    /// <summary>Image aspect ratio (width / height), or 1 when no image is set.</summary>
    public double AspectRatio => ImageWidth > 0 && ImageHeight > 0
        ? (double)ImageWidth / ImageHeight
        : 1.0;

    public bool HasMarker(int zoneId) => Markers.Any(m => m.ZoneId == zoneId);

    /// <summary>
    /// Places the marker for <paramref name="zoneId"/> at the given normalized point (moving it if
    /// the zone already has one). Coordinates are clamped to [0,1]; NaN collapses to 0.
    /// </summary>
    public void Place(int zoneId, double x, double y)
    {
        var cx = Clamp01(x);
        var cy = Clamp01(y);

        var existing = Markers.FirstOrDefault(m => m.ZoneId == zoneId);
        if (existing is null)
        {
            Markers.Add(new MarkerRow { ZoneId = zoneId, X = cx, Y = cy });
        }
        else
        {
            existing.X = cx;
            existing.Y = cy;
        }
    }

    /// <summary>Removes the marker for a zone, if any.</summary>
    public void Remove(int zoneId) => Markers.RemoveAll(m => m.ZoneId == zoneId);

    private static double Clamp01(double value) =>
        double.IsNaN(value) ? 0.0 : Math.Clamp(value, 0.0, 1.0);

    public static PropertyMapEditModel FromEntity(PropertyMap map) => new()
    {
        ImagePath = map.ImagePath,
        ImageHash = map.ImageHash,
        ImageWidth = map.ImageWidth,
        ImageHeight = map.ImageHeight,
        Markers = map.Markers
            .Select(m => new MarkerRow { ZoneId = m.ZoneId, X = m.X, Y = m.Y })
            .ToList(),
    };

    public IReadOnlyList<MapMarker> ToEntity() => Markers
        .Select(m => new MapMarker { ZoneId = m.ZoneId, X = m.X, Y = m.Y })
        .ToList();

    public sealed class MarkerRow
    {
        public int ZoneId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }
}
