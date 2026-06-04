using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OSPi.Domain.Entities;
using OSPi.Domain.Enums;
using Program = OSPi.Domain.Entities.Program;

namespace OSPi.Tests.Persistence;

public class SeedAndMappingTests
{
    [Fact]
    public async Task Seeding_creates_16_zones_2_masters_and_one_settings_row()
    {
        await using var fx = new SqliteInMemoryFixture();
        await using var db = fx.CreateContext();

        var bits = await db.Zones.Select(z => z.HardwareBit).OrderBy(b => b).ToListAsync();
        bits.Should().Equal(Enumerable.Range(0, 16));

        (await db.MasterStations.Select(m => m.MasterIndex).OrderBy(i => i).ToListAsync())
            .Should().Equal(1, 2);

        (await db.ControllerSettings.CountAsync()).Should().Be(1);
        var settings = await db.ControllerSettings.SingleAsync();
        settings.WaterLevelPercent.Should().Be(100);
        settings.UseWeather.Should().BeTrue();
    }

    [Fact]
    public async Task Zone_round_trips_through_a_fresh_context()
    {
        await using var fx = new SqliteInMemoryFixture();

        await using (var write = fx.CreateContext())
        {
            var zone = await write.Zones.FirstAsync(z => z.HardwareBit == 0);
            zone.Name = "Front Lawn";
            zone.Group = ZoneGroup.Sequential2;
            zone.BoundToMaster1 = true;
            zone.IgnoreRain = true;
            await write.SaveChangesAsync();
        }

        await using var read = fx.CreateContext();
        var reloaded = await read.Zones.AsNoTracking().FirstAsync(z => z.HardwareBit == 0);
        reloaded.Name.Should().Be("Front Lawn");
        reloaded.Group.Should().Be(ZoneGroup.Sequential2);   // proves enum<->int conversion
        reloaded.BoundToMaster1.Should().BeTrue();
        reloaded.IgnoreRain.Should().BeTrue();
    }

    [Fact]
    public async Task Program_graph_round_trips_with_start_times_and_durations()
    {
        await using var fx = new SqliteInMemoryFixture();
        int programId;

        await using (var write = fx.CreateContext())
        {
            var program = new Program
            {
                Name = "Morning",
                ScheduleType = ScheduleType.Weekly,
                WeekdayMask = 0b0101010,
                StartTimeType = StartTimeType.Fixed,
                StartTimes =
                {
                    new ProgramStartTime { Slot = 0, Kind = StartTimeKind.FixedMinute, Value = 360 },
                    new ProgramStartTime { Slot = 1, Kind = StartTimeKind.SunriseOffset, Value = -15 },
                    new ProgramStartTime { Slot = 2, Kind = StartTimeKind.SunsetOffset, Value = 30 },
                },
                ZoneDurations =
                {
                    new ProgramZoneDuration { ZoneId = 1, DurationSeconds = 600, RunOrder = 2 },
                    new ProgramZoneDuration { ZoneId = 2, DurationSeconds = 300, RunOrder = 0 },
                    new ProgramZoneDuration { ZoneId = 3, DurationSeconds = 120, RunOrder = 1 },
                    new ProgramZoneDuration { ZoneId = 4, DurationSeconds = 90, RunOrder = 4 },
                    new ProgramZoneDuration { ZoneId = 5, DurationSeconds = 45, RunOrder = 3 },
                },
            };
            write.Programs.Add(program);
            await write.SaveChangesAsync();
            programId = program.Id;
        }

        await using var read = fx.CreateContext();
        var reloaded = await read.Programs
            .AsNoTracking()
            .Include(p => p.ZoneDurations)
            .FirstAsync(p => p.Id == programId);

        reloaded.WeekdayMask.Should().Be(0b0101010);
        reloaded.StartTimes.Should().HaveCount(3);
        reloaded.StartTimes.Single(s => s.Slot == 1).Kind.Should().Be(StartTimeKind.SunriseOffset);
        reloaded.StartTimes.Single(s => s.Slot == 1).Value.Should().Be(-15);

        reloaded.ZoneDurations.OrderBy(d => d.RunOrder).Select(d => d.ZoneId)
            .Should().Equal(2, 3, 1, 5, 4);
    }
}
