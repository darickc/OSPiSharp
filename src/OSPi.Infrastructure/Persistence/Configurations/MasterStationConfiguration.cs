using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OSPi.Domain.Entities;

namespace OSPi.Infrastructure.Persistence.Configurations;

internal sealed class MasterStationConfiguration : IEntityTypeConfiguration<MasterStation>
{
    public void Configure(EntityTypeBuilder<MasterStation> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => m.MasterIndex).IsUnique();

        builder.HasOne(m => m.Zone)
            .WithMany()
            .HasForeignKey(m => m.ZoneId)
            .OnDelete(DeleteBehavior.Restrict);

        // Two master rows, both initially unconfigured.
        builder.HasData(
            new MasterStation { Id = 1, MasterIndex = 1 },
            new MasterStation { Id = 2, MasterIndex = 2 });
    }
}
