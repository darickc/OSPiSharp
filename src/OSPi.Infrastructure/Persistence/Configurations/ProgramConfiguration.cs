using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OSPi.Domain.Entities;

namespace OSPi.Infrastructure.Persistence.Configurations;

internal sealed class ProgramConfiguration : IEntityTypeConfiguration<Program>
{
    public void Configure(EntityTypeBuilder<Program> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(64);
        builder.Property(p => p.OddEven).HasConversion<int>();
        builder.Property(p => p.ScheduleType).HasConversion<int>();
        builder.Property(p => p.StartTimeType).HasConversion<int>();

        // Up to four start times stored in their own table, owned by the program.
        builder.OwnsMany(p => p.StartTimes, st =>
        {
            st.ToTable("ProgramStartTimes");
            st.WithOwner().HasForeignKey("ProgramId");
            st.Property<int>("Id");
            st.HasKey("Id");
            st.Property(x => x.Kind).HasConversion<int>();
        });
        builder.Navigation(p => p.StartTimes).AutoInclude();

        builder.HasMany(p => p.ZoneDurations)
            .WithOne(d => d.Program)
            .HasForeignKey(d => d.ProgramId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
