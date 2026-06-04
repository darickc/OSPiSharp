using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OSPi.Domain.Entities;

namespace OSPi.Infrastructure.Persistence.Configurations;

internal sealed class ControllerSettingsConfiguration : IEntityTypeConfiguration<ControllerSettings>
{
    public void Configure(EntityTypeBuilder<ControllerSettings> builder)
    {
        builder.HasKey(s => s.Id);

        // Single seeded row with default values.
        builder.HasData(new ControllerSettings { Id = 1 });
    }
}
