using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OSPi.Application.Persistence;
using OSPi.Infrastructure.Persistence.Repositories;

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

        services.AddScoped<IZoneRepository, ZoneRepository>();
        services.AddScoped<IProgramRepository, ProgramRepository>();
        services.AddScoped<IMasterStationRepository, MasterStationRepository>();
        services.AddScoped<IControllerSettingsRepository, ControllerSettingsRepository>();
        services.AddScoped<IRunLogRepository, RunLogRepository>();
        services.AddScoped<ISchedulingDataRepository, SchedulingDataRepository>();

        return services;
    }

    /// <summary>Applies any pending migrations (creating the database file on first run).</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OSPiDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }
}
