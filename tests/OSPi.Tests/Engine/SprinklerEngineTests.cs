using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using OSPi.Application.Engine;
using OSPi.Application.Hardware;

namespace OSPi.Tests.Engine;

public class SprinklerEngineTests
{
    /// <summary>Captures the last bit array applied, standing in for hardware.</summary>
    private sealed class CapturingDriver : IZoneDriver
    {
        public CapturingDriver(int zoneCount) => ZoneCount = zoneCount;
        public int ZoneCount { get; }
        public bool[] Last { get; private set; } = Array.Empty<bool>();
        public void Apply(ReadOnlySpan<bool> zoneStates) => Last = zoneStates.ToArray();
    }

    private static async Task<(SprinklerEngine engine, CapturingDriver driver, InMemoryStateHub hub, FakeTimeProvider time)> StartEngineAsync(int zones = 16)
    {
        var driver = new CapturingDriver(zones);
        var hub = new InMemoryStateHub();
        var time = new FakeTimeProvider();
        var engine = new SprinklerEngine(driver, hub, NullLogger<SprinklerEngine>.Instance, time);

        await engine.StartAsync(CancellationToken.None);
        // The host starts ExecuteAsync on a background thread, so wait for the engine's
        // initial publish before handing control to the test.
        await WaitUntilAsync(() => hub.Latest is not null);
        return (engine, driver, hub, time);
    }

    /// <summary>
    /// After FakeTimeProvider.Advance fires a tick, the engine loop continuation resumes on
    /// the thread pool, so poll (real wall time) until the expected state appears.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Condition not met within timeout.");
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task Engine_starts_with_all_zones_off()
    {
        var (engine, driver, hub, _) = await StartEngineAsync();

        driver.Last.Should().OnlyContain(on => on == false);
        hub.Latest.Should().NotBeNull();
        hub.Latest!.Zones.Should().HaveCount(16);
        hub.Latest.Zones.Should().OnlyContain(z => z.On == false);

        await engine.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SetZone_command_turns_a_single_zone_on_after_a_tick()
    {
        var (engine, driver, hub, time) = await StartEngineAsync();

        engine.Post(new EngineCommand.SetZone(3, true));
        time.Advance(TimeSpan.FromSeconds(1)); // fire one tick
        await WaitUntilAsync(() => driver.Last.Length == 16 && driver.Last[3]);

        driver.Last[3].Should().BeTrue();
        driver.Last.Count(on => on).Should().Be(1);
        hub.Latest!.Zones[3].On.Should().BeTrue();

        await engine.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAll_turns_every_zone_off()
    {
        var (engine, driver, hub, time) = await StartEngineAsync();

        engine.Post(new EngineCommand.SetZone(1, true));
        engine.Post(new EngineCommand.SetZone(7, true));
        time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => driver.Last.Count(on => on) == 2);

        engine.Post(new EngineCommand.StopAll());
        time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => driver.Last.All(on => !on));

        driver.Last.Should().OnlyContain(on => on == false);

        await engine.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Out_of_range_zone_is_ignored()
    {
        var (engine, driver, _, time) = await StartEngineAsync();

        engine.Post(new EngineCommand.SetZone(99, true));
        engine.Post(new EngineCommand.SetZone(-1, true));
        time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50); // let the tick drain the commands

        driver.Last.Should().OnlyContain(on => on == false);

        await engine.StopAsync(CancellationToken.None);
    }
}
