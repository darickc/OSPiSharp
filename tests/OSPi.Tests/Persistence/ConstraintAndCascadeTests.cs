using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OSPi.Domain.Entities;
using OSPi.Domain.Enums;
using Program = OSPi.Domain.Entities.Program;

namespace OSPi.Tests.Persistence;

public class ConstraintAndCascadeTests
{
    [Fact]
    public async Task Duplicate_zone_in_one_program_violates_unique_index()
    {
        await using var fx = new SqliteInMemoryFixture();
        await using var db = fx.CreateContext();

        var program = new Program { Name = "Dup" };
        program.ZoneDurations.Add(new ProgramZoneDuration { ZoneId = 1, DurationSeconds = 60 });
        program.ZoneDurations.Add(new ProgramZoneDuration { ZoneId = 1, DurationSeconds = 90 });
        db.Programs.Add(program);

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Duplicate_hardware_bit_violates_unique_index()
    {
        await using var fx = new SqliteInMemoryFixture();
        await using var db = fx.CreateContext();

        db.Zones.Add(new Zone { Name = "Clash", HardwareBit = 0, Group = ZoneGroup.Parallel });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Deleting_a_program_cascades_durations_and_start_times_but_nulls_run_log()
    {
        await using var fx = new SqliteInMemoryFixture();
        int programId;

        await using (var seed = fx.CreateContext())
        {
            var program = new Program
            {
                Name = "ToDelete",
                StartTimes = { new ProgramStartTime { Slot = 0, Value = 300 } },
                ZoneDurations = { new ProgramZoneDuration { ZoneId = 1, DurationSeconds = 120 } },
            };
            seed.Programs.Add(program);
            await seed.SaveChangesAsync();
            programId = program.Id;

            seed.RunLog.Add(new RunLogEntry
            {
                ZoneId = 1,
                ProgramId = programId,
                StartTime = DateTimeOffset.UnixEpoch,
                EndTime = DateTimeOffset.UnixEpoch.AddMinutes(2),
                DurationSeconds = 120,
            });
            await seed.SaveChangesAsync();
        }

        await using (var del = fx.CreateContext())
        {
            var program = await del.Programs.FirstAsync(p => p.Id == programId);
            del.Programs.Remove(program);
            await del.SaveChangesAsync();
        }

        await using var check = fx.CreateContext();
        (await check.ProgramZoneDurations.CountAsync(d => d.ProgramId == programId)).Should().Be(0);

        // ProgramStartTime is an owned type (no root DbSet); count its table directly.
        var startTimeRows = await check.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ProgramStartTimes")
            .SingleAsync();
        startTimeRows.Should().Be(0);

        var log = await check.RunLog.SingleAsync();
        log.ProgramId.Should().BeNull();   // history retained, reference nulled
        log.ZoneId.Should().Be(1);
    }
}
