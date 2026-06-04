using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OSPi.Domain.Entities;

namespace OSPi.Infrastructure.Persistence.Configurations;

internal sealed class RunLogEntryConfiguration : IEntityTypeConfiguration<RunLogEntry>
{
    public void Configure(EntityTypeBuilder<RunLogEntry> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasOne(e => e.Zone)
            .WithMany()
            .HasForeignKey(e => e.ZoneId)
            .OnDelete(DeleteBehavior.Restrict);

        // Keep history when a program is deleted; just null out the reference.
        builder.HasOne(e => e.Program)
            .WithMany()
            .HasForeignKey(e => e.ProgramId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.EndTime);
    }
}
