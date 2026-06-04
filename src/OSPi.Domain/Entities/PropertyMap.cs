namespace OSPi.Domain.Entities;

/// <summary>
/// The single property map: a photo or diagram of the property with one pin per zone.
/// Like <see cref="ControllerSettings"/> this is a one-row config entity (Id = 1, seeded);
/// the image itself lives as a file in writable app-data (the Pi's binary dir may be
/// read-only), and only its path/hash/dimensions are persisted here.
/// </summary>
public sealed class PropertyMap
{
    /// <summary>Surrogate primary key. Always 1 — there is exactly one property map.</summary>
    public int Id { get; set; }

    /// <summary>Path (relative to the image store) of the re-encoded image, or null until one is uploaded.</summary>
    public string? ImagePath { get; set; }

    /// <summary>SHA-256 of the re-encoded image bytes, used for HTTP cache-busting; null until uploaded.</summary>
    public string? ImageHash { get; set; }

    /// <summary>Stored image width in pixels (after re-encode); 0 until uploaded.</summary>
    public int ImageWidth { get; set; }

    /// <summary>Stored image height in pixels (after re-encode); 0 until uploaded.</summary>
    public int ImageHeight { get; set; }

    /// <summary>Zone markers placed on the map (at most one per zone).</summary>
    public ICollection<MapMarker> Markers { get; set; } = new List<MapMarker>();
}
