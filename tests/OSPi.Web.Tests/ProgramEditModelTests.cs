using FluentAssertions;
using OSPi.Web.Models;
using ProgramEntity = OSPi.Domain.Entities.Program;
using ProgramZoneDuration = OSPi.Domain.Entities.ProgramZoneDuration;

namespace OSPi.Web.Tests;

/// <summary>
/// View-model logic for Phase 4 (custom run order + bulk edit). The drag interop itself is
/// browser-only, but reordering, bulk-apply, and the RunOrder = list-position contract all
/// live in <see cref="ProgramEditModel"/> and are unit-testable here.
/// </summary>
public class ProgramEditModelTests
{
    private static ProgramEditModel ModelWithZones(params int[] zoneIds)
    {
        var model = new ProgramEditModel { Name = "Test" };
        foreach (var id in zoneIds)
        {
            model.ZoneDurations.Add(new ProgramEditModel.ZoneDurationRow
            {
                ZoneId = id,
                DurationSeconds = 60 * id, // distinct, so we can track identity
            });
        }

        return model;
    }

    [Fact]
    public void ToEntity_numbers_RunOrder_by_list_position()
    {
        var model = ModelWithZones(3, 1, 2);

        var entity = model.ToEntity();

        entity.ZoneDurations.Select(d => (d.ZoneId, d.RunOrder))
            .Should().Equal((3, 0), (1, 1), (2, 2));
    }

    [Fact]
    public void MoveZone_reorders_and_ToEntity_renumbers_to_match()
    {
        var model = ModelWithZones(1, 2, 3, 4);

        // Drag the first row (zone 1) down to index 2.
        model.MoveZone(0, 2);

        model.ZoneDurations.Select(d => d.ZoneId).Should().Equal(2, 3, 1, 4);

        var entity = model.ToEntity();
        entity.ZoneDurations.Select(d => (d.ZoneId, d.RunOrder))
            .Should().Equal((2, 0), (3, 1), (1, 2), (4, 3));
    }

    [Fact]
    public void MoveZone_to_index_zero_saves_RunOrder_zero_not_a_default()
    {
        // Regression: ToEntity used to special-case RunOrder == 0 and re-default it to the
        // list index. A row dragged to the front must save with RunOrder 0 explicitly.
        var model = ModelWithZones(5, 6, 7);

        model.MoveZone(2, 0); // zone 7 to the front

        var entity = model.ToEntity();
        entity.ZoneDurations.Single(d => d.ZoneId == 7).RunOrder.Should().Be(0);
        entity.ZoneDurations.Select(d => d.ZoneId).Should().Equal(7, 5, 6);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 5)]
    [InlineData(2, 2)]
    public void MoveZone_ignores_out_of_range_or_no_op_moves(int from, int to)
    {
        var model = ModelWithZones(1, 2, 3);

        model.MoveZone(from, to);

        model.ZoneDurations.Select(d => d.ZoneId).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void ApplyDurationToSelected_changes_only_selected_rows()
    {
        var model = ModelWithZones(1, 2, 3);
        model.ZoneDurations[0].Selected = true;
        model.ZoneDurations[2].Selected = true;

        model.ApplyDurationToSelected(900);

        model.ZoneDurations[0].DurationSeconds.Should().Be(900);
        model.ZoneDurations[1].DurationSeconds.Should().Be(120); // untouched (60 * 2)
        model.ZoneDurations[2].DurationSeconds.Should().Be(900);
    }

    [Fact]
    public void FromEntity_loads_rows_in_RunOrder_then_Move_and_ToEntity_round_trip()
    {
        var program = new ProgramEntity
        {
            Name = "RoundTrip",
            ScheduleType = OSPi.Domain.Enums.ScheduleType.Weekly,
            WeekdayMask = 0b0000001, // Monday — a valid schedule so Validate() is clean
            ZoneDurations =
            {
                new ProgramZoneDuration { ZoneId = 1, DurationSeconds = 100, RunOrder = 2 },
                new ProgramZoneDuration { ZoneId = 2, DurationSeconds = 200, RunOrder = 0 },
                new ProgramZoneDuration { ZoneId = 3, DurationSeconds = 300, RunOrder = 1 },
            },
        };

        var model = ProgramEditModel.FromEntity(program);

        // Loaded in RunOrder: zone 2, 3, 1.
        model.ZoneDurations.Select(d => d.ZoneId).Should().Equal(2, 3, 1);

        // Move zone 1 to the front, then save.
        model.MoveZone(2, 0);
        var entity = model.ToEntity();

        entity.ZoneDurations.OrderBy(d => d.RunOrder).Select(d => d.ZoneId)
            .Should().Equal(1, 2, 3);

        // Still valid under the existing rules (unique zones, durations > 0).
        var ctx = new System.ComponentModel.DataAnnotations.ValidationContext(model);
        model.Validate(ctx).Should().BeEmpty();
    }
}
