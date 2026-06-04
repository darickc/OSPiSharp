using FluentAssertions;
using OSPi.Domain.Entities;
using OSPi.Web.Models;

namespace OSPi.Web.Tests;

/// <summary>
/// View-model logic for Phase 5 (property map). Click-to-place is browser-only, but the
/// coordinate clamp, one-marker-per-zone placement/move/remove, and the FromEntity/ToEntity
/// round-trip all live in <see cref="PropertyMapEditModel"/> and are unit-testable here.
/// </summary>
public class PropertyMapEditModelTests
{
    [Fact]
    public void Place_adds_a_marker_for_a_new_zone()
    {
        var model = new PropertyMapEditModel();

        model.Place(zoneId: 5, x: 0.25, y: 0.75);

        model.Markers.Should().ContainSingle();
        var marker = model.Markers[0];
        (marker.ZoneId, marker.X, marker.Y).Should().Be((5, 0.25, 0.75));
    }

    [Fact]
    public void Place_moves_the_existing_marker_for_a_zone_rather_than_adding()
    {
        var model = new PropertyMapEditModel();
        model.Place(5, 0.1, 0.1);

        model.Place(5, 0.6, 0.4);

        model.Markers.Should().ContainSingle();
        (model.Markers[0].X, model.Markers[0].Y).Should().Be((0.6, 0.4));
    }

    [Theory]
    [InlineData(-0.5, 1.5, 0.0, 1.0)]
    [InlineData(2.0, -3.0, 1.0, 0.0)]
    public void Place_clamps_coordinates_into_the_unit_square(double x, double y, double expectedX, double expectedY)
    {
        var model = new PropertyMapEditModel();

        model.Place(1, x, y);

        (model.Markers[0].X, model.Markers[0].Y).Should().Be((expectedX, expectedY));
    }

    [Fact]
    public void Place_treats_NaN_as_zero()
    {
        var model = new PropertyMapEditModel();

        model.Place(1, double.NaN, double.NaN);

        (model.Markers[0].X, model.Markers[0].Y).Should().Be((0.0, 0.0));
    }

    [Fact]
    public void Remove_deletes_only_the_named_zone_marker()
    {
        var model = new PropertyMapEditModel();
        model.Place(1, 0.1, 0.1);
        model.Place(2, 0.2, 0.2);

        model.Remove(1);

        model.HasMarker(1).Should().BeFalse();
        model.HasMarker(2).Should().BeTrue();
    }

    [Fact]
    public void AspectRatio_uses_stored_dimensions_and_defaults_to_one()
    {
        new PropertyMapEditModel { ImageWidth = 1600, ImageHeight = 800 }.AspectRatio.Should().Be(2.0);
        new PropertyMapEditModel().AspectRatio.Should().Be(1.0);
    }

    [Fact]
    public void FromEntity_then_ToEntity_round_trips_image_metadata_and_markers()
    {
        var entity = new PropertyMap
        {
            Id = 1,
            ImagePath = "abc.jpg",
            ImageHash = "abc",
            ImageWidth = 1200,
            ImageHeight = 900,
            Markers =
            {
                new MapMarker { ZoneId = 3, X = 0.3, Y = 0.4 },
                new MapMarker { ZoneId = 7, X = 0.7, Y = 0.8 },
            },
        };

        var model = PropertyMapEditModel.FromEntity(entity);
        model.HasImage.Should().BeTrue();
        model.ImageWidth.Should().Be(1200);
        model.Markers.Should().HaveCount(2);

        // Move one pin, then map back out.
        model.Place(3, 0.35, 0.45);
        var markers = model.ToEntity();

        markers.Should().HaveCount(2);
        markers.Select(m => m.ZoneId).Should().BeEquivalentTo(new[] { 3, 7 });
        var moved = markers.Single(m => m.ZoneId == 3);
        (moved.X, moved.Y).Should().Be((0.35, 0.45));
    }

    [Fact]
    public void HasImage_is_false_before_an_upload()
    {
        new PropertyMapEditModel().HasImage.Should().BeFalse();
    }
}
