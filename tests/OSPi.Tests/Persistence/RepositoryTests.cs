using FluentAssertions;
using OSPi.Domain.Entities;
using OSPi.Domain.Enums;
using OSPi.Infrastructure.Persistence.Repositories;
using Program = OSPi.Domain.Entities.Program;

namespace OSPi.Tests.Persistence;

public class RepositoryTests
{
    [Fact]
    public async Task ProgramRepository_supports_add_get_update_delete()
    {
        await using var fx = new SqliteInMemoryFixture();
        var repo = new ProgramRepository(fx.Factory);

        // Add
        var program = new Program
        {
            Name = "Evening",
            ScheduleType = ScheduleType.Interval,
            IntervalDays = 3,
            ZoneDurations =
            {
                new ProgramZoneDuration { ZoneId = 1, DurationSeconds = 300, RunOrder = 0 },
                new ProgramZoneDuration { ZoneId = 2, DurationSeconds = 600, RunOrder = 1 },
            },
        };
        var id = await repo.AddAsync(program);
        id.Should().BeGreaterThan(0);

        // Get with details
        var loaded = await repo.GetWithDetailsAsync(id);
        loaded.Should().NotBeNull();
        loaded!.ZoneDurations.Should().HaveCount(2);

        // Update: change a duration, drop zone 2, add zone 3
        loaded.Name = "Evening (edited)";
        var z1 = loaded.ZoneDurations.Single(d => d.ZoneId == 1);
        z1.DurationSeconds = 450;
        loaded.ZoneDurations.Remove(loaded.ZoneDurations.Single(d => d.ZoneId == 2));
        loaded.ZoneDurations.Add(new ProgramZoneDuration { ZoneId = 3, DurationSeconds = 120, RunOrder = 2 });
        await repo.UpdateAsync(loaded);

        var afterUpdate = await repo.GetWithDetailsAsync(id);
        afterUpdate!.Name.Should().Be("Evening (edited)");
        afterUpdate.ZoneDurations.Select(d => d.ZoneId).OrderBy(x => x).Should().Equal(1, 3);
        afterUpdate.ZoneDurations.Single(d => d.ZoneId == 1).DurationSeconds.Should().Be(450);

        // Delete
        await repo.DeleteAsync(id);
        (await repo.GetWithDetailsAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task ControllerSettingsRepository_reads_and_updates_the_single_row()
    {
        await using var fx = new SqliteInMemoryFixture();
        var repo = new ControllerSettingsRepository(fx.Factory);

        var settings = await repo.GetAsync();
        settings.WaterLevelPercent.Should().Be(100);

        settings.WaterLevelPercent = 80;
        settings.StationDelaySeconds = 15;
        settings.RainDelayUntil = DateTimeOffset.UnixEpoch.AddHours(6);
        await repo.UpdateAsync(settings);

        var reloaded = await repo.GetAsync();
        reloaded.WaterLevelPercent.Should().Be(80);
        reloaded.StationDelaySeconds.Should().Be(15);
        reloaded.RainDelayUntil.Should().Be(DateTimeOffset.UnixEpoch.AddHours(6));
    }

    [Fact]
    public async Task ZoneRepository_lists_in_hardware_order_and_persists_edits()
    {
        await using var fx = new SqliteInMemoryFixture();
        var repo = new ZoneRepository(fx.Factory);

        var zones = await repo.GetAllAsync();
        zones.Select(z => z.HardwareBit).Should().BeInAscendingOrder();
        zones.Should().HaveCount(16);

        var zone = await repo.GetAsync(1);
        zone!.Name = "Garden";
        zone.Group = ZoneGroup.Independent;
        await repo.UpdateAsync(zone);

        (await repo.GetAsync(1))!.Group.Should().Be(ZoneGroup.Independent);
    }
}
