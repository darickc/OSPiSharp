using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OSPi.Domain.Entities;

namespace OSPi.Infrastructure.Persistence.Configurations;

internal sealed class PropertyMapConfiguration : IEntityTypeConfiguration<PropertyMap>
{
    public void Configure(EntityTypeBuilder<PropertyMap> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.ImagePath).HasMaxLength(260);
        builder.Property(m => m.ImageHash).HasMaxLength(64);

        builder.HasMany(m => m.Markers)
            .WithOne(k => k.PropertyMap)
            .HasForeignKey(k => k.PropertyMapId)
            .OnDelete(DeleteBehavior.Cascade);

        // Single seeded row; image is uploaded later (ImagePath stays null).
        builder.HasData(new PropertyMap { Id = 1 });
    }
}
