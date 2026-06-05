using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OSPi.Application.Persistence;
using OSPi.Application.Services;
using OSPi.Infrastructure.Persistence.Repositories;
using OSPi.Infrastructure.Services;

namespace OSPi.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite-backed <see cref="OSPiDbContext"/> as a pooled context factory
    /// (the correct pattern for Blazor Server — short-lived contexts per operation) and the
    /// repository implementations behind their Application-layer interfaces.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new DatabaseOptions();
        configuration.GetSection(DatabaseOptions.SectionName).Bind(options);

        services.AddDbContextFactory<OSPiDbContext>(o => o.UseSqlite(options.BuildConnectionString()));

        // Writable directory for uploaded property-map images (resolved like the SQLite path).
        var imageStorage = new ImageStorageOptions();
        configuration.GetSection(ImageStorageOptions.SectionName).Bind(imageStorage);
        services.AddSingleton(imageStorage);

        services.AddScoped<IZoneRepository, ZoneRepository>();
        services.AddScoped<IProgramRepository, ProgramRepository>();
        services.AddScoped<IMasterStationRepository, MasterStationRepository>();
        services.AddScoped<IControllerSettingsRepository, ControllerSettingsRepository>();
        services.AddScoped<IRunLogRepository, RunLogRepository>();
        services.AddScoped<ISchedulingDataRepository, SchedulingDataRepository>();
        services.AddScoped<IPropertyMapRepository, PropertyMapRepository>();

        // Stateless ImageSharp re-encoder.
        services.AddSingleton<IPropertyMapImageProcessor, PropertyMapImageProcessor>();

        return services;
    }

    /// <summary>Applies any pending migrations (creating the database file on first run).</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OSPiDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);

        // First run after the timezone migration: seed the controller's zone from the host OS so
        // existing installs immediately track DST instead of the (possibly stale) fixed offset.
        var settings = await db.ControllerSettings.FirstOrDefaultAsync(ct);
        if (settings is not null && string.IsNullOrWhiteSpace(settings.TimeZoneId))
        {
            settings.TimeZoneId = TimeZoneInfo.Local.Id;
            await db.SaveChangesAsync(ct);
        }
    }
}
