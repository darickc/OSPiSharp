namespace OSPi.Infrastructure.Persistence;

/// <summary>Bound from the "Database" configuration section.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// SQLite file path. Relative paths resolve under the per-user app-data directory;
    /// absolute paths are honored as-is. When empty, defaults to
    /// <c>{LocalApplicationData}/OSPiSharp/ospi.db</c>.
    /// </summary>
    public string Path { get; set; } = "ospi.db";

    /// <summary>
    /// Resolves <see cref="Path"/> to an absolute file path, creating the containing
    /// directory if needed. The published binary directory on the Pi may be read-only,
    /// so relative paths deliberately resolve under writable app-data, not the content root.
    /// </summary>
    public string ResolveDatabasePath()
    {
        var dataRoot = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OSPiSharp");

        var configured = string.IsNullOrWhiteSpace(Path) ? "ospi.db" : Path;
        var resolved = System.IO.Path.IsPathRooted(configured)
            ? configured
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(dataRoot, configured));

        var directory = System.IO.Path.GetDirectoryName(resolved);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return resolved;
    }

    /// <summary>Builds the SQLite connection string for the resolved path.</summary>
    public string BuildConnectionString() => $"Data Source={ResolveDatabasePath()}";
}
