using FluentAssertions;
using OSPi.Domain.Entities;
using OSPi.Infrastructure.Persistence;
using OSPi.Infrastructure.Persistence.Repositories;

namespace OSPi.Tests.Persistence;

public class PropertyMapRepositoryTests
{
    private static ImageStorageOptions TempStorage() =>
        new() { Path = Path.Combine(Path.GetTempPath(), "ospi-test-" + Guid.NewGuid().ToString("N")) };

    [Fact]
    public async Task GetAsync_returns_the_seeded_row_with_no_markers()
    {
        await using var fx = new SqliteInMemoryFixture();
        var repo = new PropertyMapRepository(fx.Factory, TempStorage());

        var map = await repo.GetAsync();

        map.Id.Should().Be(1);
        map.ImagePath.Should().BeNull();
        map.Markers.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateImageAsync_persists_image_metadata()
    {
        await using var fx = new SqliteInMemoryFixture();
        var repo = new PropertyMapRepository(fx.Factory, TempStorage());

        await repo.UpdateImageAsync("hash1.jpg", "hash1", 1280, 720);

        var map = await repo.GetAsync();
        map.ImagePath.Should().Be("hash1.jpg");
        map.ImageHash.Should().Be("hash1");
        (map.ImageWidth, map.ImageHeight).Should().Be((1280, 720));
    }

    [Fact]
    public async Task SaveMarkersAsync_adds_updates_and_removes_merging_by_zone()
    {
        await using var fx = new SqliteInMemoryFixture();
        var repo = new PropertyMapRepository(fx.Factory, TempStorage());

        // Initial set: zones 1 and 2.
        await repo.SaveMarkersAsync(new[]
        {
            new MapMarker { ZoneId = 1, X = 0.1, Y = 0.1 },
            new MapMarker { ZoneId = 2, X = 0.2, Y = 0.2 },
        });

        (await repo.GetAsync()).Markers.Select(m => m.ZoneId).OrderBy(x => x).Should().Equal(1, 2);

        // Move zone 1, drop zone 2, add zone 3.
        await repo.SaveMarkersAsync(new[]
        {
            new MapMarker { ZoneId = 1, X = 0.5, Y = 0.6 },
            new MapMarker { ZoneId = 3, X = 0.3, Y = 0.3 },
        });

        var map = await repo.GetAsync();
        map.Markers.Select(m => m.ZoneId).OrderBy(x => x).Should().Equal(1, 3);
        var moved = map.Markers.Single(m => m.ZoneId == 1);
        (moved.X, moved.Y).Should().Be((0.5, 0.6));
    }

    [Fact]
    public async Task SaveMarkersAsync_preserves_marker_identity_when_updating_in_place()
    {
        await using var fx = new SqliteInMemoryFixture();
        var repo = new PropertyMapRepository(fx.Factory, TempStorage());

        await repo.SaveMarkersAsync(new[] { new MapMarker { ZoneId = 4, X = 0.1, Y = 0.1 } });
        var firstId = (await repo.GetAsync()).Markers.Single().Id;

        // Re-saving the same zone updates the existing row rather than re-inserting (no unique-index clash).
        await repo.SaveMarkersAsync(new[] { new MapMarker { ZoneId = 4, X = 0.9, Y = 0.9 } });

        var marker = (await repo.GetAsync()).Markers.Single();
        marker.Id.Should().Be(firstId);
        (marker.X, marker.Y).Should().Be((0.9, 0.9));
    }
}
