namespace OSPi.Domain.Entities;

/// <summary>
/// A pin on the <see cref="PropertyMap"/> for one zone, positioned in normalized
/// 0..1 coordinates so it survives responsive resizing. At most one marker per zone.
/// <see cref="ZoneId"/> references the zone's surrogate key (like
/// <see cref="ProgramZoneDuration.ZoneId"/>); the live-highlight and click-to-run paths
/// translate it to the hardware bit in the UI layer.
/// </summary>
public sealed class MapMarker
{
    /// <summary>Surrogate primary key.</summary>
    public int Id { get; set; }

    /// <summary>Owning property map.</summary>
    public int PropertyMapId { get; set; }

    /// <summary>Owning property map navigation.</summary>
    public PropertyMap PropertyMap { get; set; } = null!;

    /// <summary>Target zone.</summary>
    public int ZoneId { get; set; }

    /// <summary>Target zone navigation.</summary>
    public Zone Zone { get; set; } = null!;

    /// <summary>Horizontal position as a fraction of image width (0..1, left to right).</summary>
    public double X { get; set; }

    /// <summary>Vertical position as a fraction of image height (0..1, top to bottom).</summary>
    public double Y { get; set; }
}
