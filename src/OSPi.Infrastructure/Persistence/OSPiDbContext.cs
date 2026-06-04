using Microsoft.EntityFrameworkCore;
using OSPi.Domain.Entities;

namespace OSPi.Infrastructure.Persistence;

/// <summary>
/// EF Core context for all persisted controller state. Entity mapping lives in
/// <see cref="Configurations"/> classes applied via <c>ApplyConfigurationsFromAssembly</c>,
/// keeping the Domain POCOs free of persistence concerns.
/// </summary>
public sealed class OSPiDbContext : DbContext
{
    public OSPiDbContext(DbContextOptions<OSPiDbContext> options) : base(options) { }

    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<Domain.Entities.Program> Programs => Set<Domain.Entities.Program>();
    public DbSet<ProgramZoneDuration> ProgramZoneDurations => Set<ProgramZoneDuration>();
    public DbSet<MasterStation> MasterStations => Set<MasterStation>();
    public DbSet<ControllerSettings> ControllerSettings => Set<ControllerSettings>();
    public DbSet<RunLogEntry> RunLog => Set<RunLogEntry>();
    public DbSet<PropertyMap> PropertyMaps => Set<PropertyMap>();
    public DbSet<MapMarker> MapMarkers => Set<MapMarker>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(OSPiDbContext).Assembly);
}
