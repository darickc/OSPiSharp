using System.Security.Cryptography;
using OSPi.Application.Services;
using OSPi.Infrastructure.Persistence;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace OSPi.Infrastructure.Services;

/// <summary>
/// Re-encodes uploaded property-map images with SixLabors.ImageSharp (fully managed, ARM-safe):
/// downscales so the longest edge is at most <see cref="MaxEdge"/>, re-encodes to JPEG (or PNG to
/// preserve transparency), names the file by its content hash, and writes it to the image store.
/// </summary>
internal sealed class PropertyMapImageProcessor : IPropertyMapImageProcessor
{
    private const int MaxEdge = 1600;
    private const int JpegQuality = 85;

    private readonly ImageStorageOptions _storage;

    public PropertyMapImageProcessor(ImageStorageOptions storage) => _storage = storage;

    public async Task<StoredImage> SaveAsync(Stream upload, CancellationToken ct = default)
    {
        using var image = await Image.LoadAsync(upload, ct);

        // Downscale only — never upscale a small image.
        if (image.Width > MaxEdge || image.Height > MaxEdge)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(MaxEdge, MaxEdge),
            }));
        }

        var keepAlpha = image.Metadata.DecodedImageFormat is PngFormat;
        var extension = keepAlpha ? ".png" : ".jpg";

        using var encoded = new MemoryStream();
        if (keepAlpha)
        {
            await image.SaveAsPngAsync(encoded, ct);
        }
        else
        {
            await image.SaveAsJpegAsync(encoded, new JpegEncoder { Quality = JpegQuality }, ct);
        }

        var bytes = encoded.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var fileName = hash + extension;

        var directory = _storage.ResolveDirectory();
        var fullPath = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(fullPath, bytes, ct);

        return new StoredImage(fileName, hash, image.Width, image.Height);
    }
}
