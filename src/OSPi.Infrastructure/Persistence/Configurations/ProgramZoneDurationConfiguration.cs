using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OSPi.Domain.Entities;

namespace OSPi.Infrastructure.Persistence.Configurations;

internal sealed class ProgramZoneDurationConfiguration : IEntityTypeConfiguration<ProgramZoneDuration>
{
    public void Configure(EntityTypeBuilder<ProgramZoneDuration> builder)
    {
        builder.HasKey(d => d.Id);

        // One duration per zone per program.
        builder.HasIndex(d => new { d.ProgramId, d.ZoneId }).IsUnique();

        // Zones are never deleted (16 seeded rows), so restrict on that side.
        builder.HasOne(d => d.Zone)
            .WithMany(z => z.ProgramDurations)
            .HasForeignKey(d => d.ZoneId)
            .OnDelete(DeleteBehavior.Restrict);

        // The Program -> ZoneDurations cascade is configured in ProgramConfiguration.
    }
}
