using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OSPi.Domain.Entities;
using OSPi.Domain.Enums;

namespace OSPi.Infrastructure.Persistence.Configurations;

internal sealed class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> builder)
    {
        builder.HasKey(z => z.Id);
        builder.Property(z => z.HardwareBit).IsRequired();
        builder.HasIndex(z => z.HardwareBit).IsUnique();
        builder.Property(z => z.Name).IsRequired().HasMaxLength(64);
        builder.Property(z => z.Group).HasConversion<int>();

        // Seed the 16 fixed zones. HardwareBit is the immutable wiring identity (0..15);
        // Id is the relational key (bit + 1).
        builder.HasData(Enumerable.Range(0, 16).Select(bit => new Zone
        {
            Id = bit + 1,
            HardwareBit = bit,
            Name = $"Zone {bit + 1}",
            Group = ZoneGroup.Sequential0,
        }));
    }
}
