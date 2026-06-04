using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OSPi.Application.Engine;
using OSPi.Application.Hardware;
using OSPi.Application.Services;
using OSPi.Infrastructure.Hardware;
using OSPi.Infrastructure.Persistence;

namespace OSPi.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence (SQLite context factory + repositories), the hardware driver
    /// (selected by config), the state hub, the sprinkler engine (as both a singleton and a
    /// hosted service), and the application services.
    /// </summary>
    public static IServiceCollection AddSprinklerCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistence(configuration);

        services.Configure<HardwareOptions>(configuration.GetSection(HardwareOptions.SectionName));

        services.AddSingleton<IZoneDriver>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<HardwareOptions>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            return opts.Driver.Equals("ShiftRegister", StringComparison.OrdinalIgnoreCase)
                ? new ShiftRegisterZoneDriver(opts.ZoneCount, opts.ShiftRegister,
                    loggerFactory.CreateLogger<ShiftRegisterZoneDriver>())
                : new SimZoneDriver(opts.ZoneCount, loggerFactory.CreateLogger<SimZoneDriver>());
        });

        services.AddSingleton<IStateHub, InMemoryStateHub>();

        // Pure sunrise/sunset math — stateless, safe as a singleton.
        services.AddSingleton<ISolarCalculator, SolarCalculatorService>();

        // One engine instance shared by the hosted service and the services that post to it.
        services.AddSingleton<SprinklerEngine>();
        services.AddHostedService(sp => sp.GetRequiredService<SprinklerEngine>());

        services.AddSingleton<IManualRunService, ManualRunService>();

        return services;
    }
}
