using FluentAssertions;
using OSPi.Infrastructure.Persistence;
using OSPi.Infrastructure.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OSPi.Tests.Persistence;

/// <summary>
/// Exercises the real SixLabors.ImageSharp pipeline (decode → downscale → re-encode → hash →
/// write) end to end, confirming the dependency works on this platform/runtime.
/// </summary>
public class PropertyMapImageProcessorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ospi-img-" + Guid.NewGuid().ToString("N"));

    private PropertyMapImageProcessor CreateProcessor() =>
        new(new ImageStorageOptions { Path = _dir });

    [Fact]
    public async Task SaveAsync_downscales_oversized_jpeg_and_writes_a_hashed_file()
    {
        var processor = CreateProcessor();

        using var source = new MemoryStream();
        using (var image = new Image<Rgba32>(3000, 2000))
        {
            await image.SaveAsJpegAsync(source);
        }
        source.Position = 0;

        var stored = await processor.SaveAsync(source);

        // Longest edge bounded to 1600, aspect preserved (3000x2000 -> 1600x1067).
        stored.Width.Should().Be(1600);
        stored.Height.Should().Be(1067);
        stored.RelativePath.Should().Be(stored.Hash + ".jpg");
        stored.Hash.Should().HaveLength(64); // SHA-256 hex
        File.Exists(Path.Combine(_dir, stored.RelativePath)).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_keeps_small_png_dimensions_and_preserves_png_format()
    {
        var processor = CreateProcessor();

        using var source = new MemoryStream();
        using (var image = new Image<Rgba32>(400, 300))
        {
            await image.SaveAsPngAsync(source);
        }
        source.Position = 0;

        var stored = await processor.SaveAsync(source);

        (stored.Width, stored.Height).Should().Be((400, 300)); // not upscaled
        stored.RelativePath.Should().EndWith(".png"); // alpha-capable source kept as PNG
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
