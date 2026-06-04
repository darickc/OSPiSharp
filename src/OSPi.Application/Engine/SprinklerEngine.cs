using System.Collections.Immutable;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OSPi.Application.Hardware;

namespace OSPi.Application.Engine;

/// <summary>
/// The single owner of mutable runtime state and the only writer to hardware. Runs a
/// once-per-second tick loop: drains queued <see cref="EngineCommand"/>s, computes the
/// desired state of all zones, applies it to the <see cref="IZoneDriver"/>, and publishes
/// a <see cref="StatusSnapshot"/>.
///
/// Phase 0 scope: manual zone control only. Program matching and scheduling are added in
/// Phase 2; their state will be owned by this same loop.
/// </summary>
public sealed class SprinklerEngine : BackgroundService
{
    private readonly IZoneDriver _driver;
    private readonly IStateHub _stateHub;
    private readonly ILogger<SprinklerEngine> _logger;
    private readonly TimeProvider _clock;

    private readonly Channel<EngineCommand> _commands =
        Channel.CreateUnbounded<EngineCommand>(new UnboundedChannelOptions
        {
            SingleReader = true
        });

    /// <summary>Desired on/off state per zone. Mutated only on the loop thread.</summary>
    private readonly bool[] _desired;

    public SprinklerEngine(
        IZoneDriver driver,
        IStateHub stateHub,
        ILogger<SprinklerEngine> logger,
        TimeProvider? clock = null)
    {
        _driver = driver;
        _stateHub = stateHub;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _desired = new bool[driver.ZoneCount];
    }

    /// <summary>Post a command to be applied at the top of the next tick.</summary>
    public void Post(EngineCommand command) => _commands.Writer.TryWrite(command);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SprinklerEngine started with {ZoneCount} zones.", _driver.ZoneCount);

        // Ensure hardware starts in a known (all-off) state.
        _driver.Apply(_desired);
        Publish();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _clock);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                DrainCommands();
                Tick();
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        finally
        {
            Array.Clear(_desired);
            _driver.Apply(_desired);
            _logger.LogInformation("SprinklerEngine stopped; all zones turned off.");
        }
    }

    private void DrainCommands()
    {
        while (_commands.Reader.TryRead(out var command))
        {
            switch (command)
            {
                case EngineCommand.SetZone set when IsValidZone(set.ZoneId):
                    _desired[set.ZoneId] = set.On;
                    break;
                case EngineCommand.SetZone set:
                    _logger.LogWarning("Ignoring SetZone for out-of-range zone {ZoneId}.", set.ZoneId);
                    break;
                case EngineCommand.StopAll:
                    Array.Clear(_desired);
                    break;
            }
        }
    }

    private void Tick()
    {
        // Phase 2 will compute _desired from the program queue here.
        _driver.Apply(_desired);
        Publish();
    }

    private bool IsValidZone(int zoneId) => zoneId >= 0 && zoneId < _desired.Length;

    private void Publish()
    {
        var zones = ImmutableArray.CreateBuilder<ZoneStatus>(_desired.Length);
        for (var i = 0; i < _desired.Length; i++)
        {
            zones.Add(new ZoneStatus { ZoneId = i, On = _desired[i] });
        }

        _stateHub.Publish(new StatusSnapshot
        {
            TimestampUtc = _clock.GetUtcNow(),
            Zones = zones.MoveToImmutable()
        });
    }
}
