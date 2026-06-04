namespace OSPi.Application.Services;

/// <summary>
/// Re-encodes and stores an uploaded property-map image, returning the metadata to persist.
/// The implementation downscales oversized images, re-encodes to a bounded format, writes the
/// result to the writable image store, and computes a content hash for cache-busting.
/// </summary>
public interface IPropertyMapImageProcessor
{
    /// <summary>
    /// Processes <paramref name="upload"/> and stores it. Returns the store-relative path, the
    /// SHA-256 hash of the stored bytes, and the final pixel dimensions.
    /// </summary>
    Task<StoredImage> SaveAsync(Stream upload, CancellationToken ct = default);
}

/// <summary>Metadata for an image stored by <see cref="IPropertyMapImageProcessor"/>.</summary>
public readonly record struct StoredImage(string RelativePath, string Hash, int Width, int Height);
