namespace OSPi.Infrastructure.Persistence;

/// <summary>Bound from the "ImageStorage" configuration section.</summary>
public sealed class ImageStorageOptions
{
    public const string SectionName = "ImageStorage";

    /// <summary>
    /// Directory for uploaded property-map images. Relative paths resolve under the per-user
    /// app-data directory; absolute paths are honored as-is. When empty, defaults to
    /// <c>{LocalApplicationData}/OSPiSharp/property-map</c>. Like the SQLite file, this lives in
    /// writable app-data because the Pi's published binary directory may be read-only.
    /// </summary>
    public string Path { get; set; } = "property-map";

    /// <summary>Resolves <see cref="Path"/> to an absolute directory, creating it if needed.</summary>
    public string ResolveDirectory()
    {
        var dataRoot = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OSPiSharp");

        var configured = string.IsNullOrWhiteSpace(Path) ? "property-map" : Path;
        var resolved = System.IO.Path.IsPathRooted(configured)
            ? configured
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(dataRoot, configured));

        Directory.CreateDirectory(resolved);
        return resolved;
    }
}
