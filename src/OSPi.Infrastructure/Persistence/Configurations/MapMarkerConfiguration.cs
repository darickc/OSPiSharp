using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OSPi.Domain.Entities;

namespace OSPi.Infrastructure.Persistence.Configurations;

internal sealed class MapMarkerConfiguration : IEntityTypeConfiguration<MapMarker>
{
    public void Configure(EntityTypeBuilder<MapMarker> builder)
    {
        builder.HasKey(m => m.Id);

        // At most one marker per zone on the map.
        builder.HasIndex(m => new { m.PropertyMapId, m.ZoneId }).IsUnique();

        builder.HasOne(m => m.Zone)
            .WithMany()
            .HasForeignKey(m => m.ZoneId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
