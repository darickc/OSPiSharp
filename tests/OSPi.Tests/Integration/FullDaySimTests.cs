using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using OSPi.Application.Engine;
using OSPi.Application.Persistence;
using OSPi.Domain.Entities;
using OSPi.Domain.Enums;
using OSPi.Tests.Engine;

namespace OSPi.Tests.Integration;

/// <summary>
/// The Phase 2 exit gate: replay a simulated timeline second-by-second through the real engine
/// (Sim driver + FakeTimeProvider) and diff the on/off transition log against a hand-computed
/// golden log. Edges are expressed in seconds-since-anchor; civil time = anchor + offset.
/// </summary>
public class FullDaySimTests
{
    /// <summary>A driver on/off transition: at <c>Second</c> (seconds since anchor), <c>Bit</c> went <c>On</c>.</summary>
    private readonly record struct Edge(int Second, int Bit, bool On);

    /// <summary>Drives the engine tick-by-tick and records only transition edges (a golden-log harness).</summary>
    private sealed class DayRunner
    {
        private readonly SprinklerEngine _engine;
        private readonly CapturingDriver _driver;
        private readonly FakeTimeProvider _time;
        private bool[] _prev;

        public List<Edge> Edges { get; } = new();

        public DayRunner(SchedulingData data, DateTimeOffset anchor, int zones = 16)
        {
            _driver = new CapturingDriver(zones);
            _time = new FakeTimeProvider(anchor);
            var scopeFactory = new FakeScopeFactory(new Dictionary<Type, object>
            {
                [typeof(ISchedulingDataRepository)] = new FakeSchedulingDataRepository(data),
                [typeof(IProgramRepository)] = new RecordingProgramRepository(),
                [typeof(IControllerSettingsRepository)] = new FakeControllerSettingsRepository(),
            });
            _engine = new SprinklerEngine(_driver, new InMemoryStateHub(), NullLogger<SprinklerEngine>.Instance,
                scopeFactory, new FixedSolarCalculator(), clock: _time);
            _engine.PrimeForTest(data);
            _prev = new bool[zones];
        }

        public void Run(int totalSeconds)
        {
            for (int k = 1; k <= totalSeconds; k++)
            {
                _time.Advance(TimeSpan.FromSeconds(1));
                _engine.Tick();
                var now = _driver.Last;
                for (int bit = 0; bit < now.Length; bit++)
                    if (now[bit] != _prev[bit])
                        Edges.Add(new Edge(k, bit, now[bit]));
                _prev = (bool[])now.Clone();
            }
        }
    }

    private static readonly DateTimeOffset Midnight = new(2024, 6, 3, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Full_day_fires_each_program_at_its_time_and_nothing_else()
    {
        var data = Build.Data(
            Build.Settings(),
            Build.Zones16(),
            new[]
            {
                Build.FixedDaily(1, 360, (1, 60, 0)),    // 06:00 zone 1 for 60s
                Build.FixedDaily(2, 1080, (2, 60, 0)),   // 18:00 zone 2 for 60s
            });
        var runner = new DayRunner(data, Midnight);

        runner.Run(86_400); // a full simulated day

        // Each single-zone sequential program starts 1s after its match minute and runs 60s.
        runner.Edges.Should().Equal(
            new Edge(21_601, 0, true),  // 06:00:01
            new Edge(21_661, 0, false), // 06:01:01
            new Edge(64_801, 1, true),  // 18:00:01
            new Edge(64_861, 1, false)); // 18:01:01
    }

    [Fact]
    public void Overnight_run_crosses_midnight()
    {
        // Anchor at 23:49:00; a program at 23:50 runs zone 1 for 20 minutes, ending 00:10 next day.
        var anchor = new DateTimeOffset(2024, 6, 2, 23, 49, 0, TimeSpan.Zero);
        var data = Build.Data(
            Build.Settings(),
            Build.Zones16(),
            new[] { Build.FixedDaily(1, 1430, (1, 1200, 0)) });
        var runner = new DayRunner(data, anchor);

        runner.Run(1300); // ~21.7 minutes, past midnight

        const int secondsToMidnight = 660; // 86400 - 85740 (23:49:00)
        runner.Edges.Should().Equal(
            new Edge(61, 0, true),     // 23:50:01
            new Edge(1261, 0, false)); // 00:10:01 next day
        runner.Edges[1].Second.Should().BeGreaterThan(secondsToMidnight, "the run ends after midnight");
    }

    [Fact]
    public void Golden_log_sequential_zones_with_master_lead_lag_and_zero_coercion()
    {
        // Anchor 05:59:00 so the 06:00 match lands ~60 ticks in. Two sequential zones (60s each,
        // 10s station delay) and a master bound to both with 0 adjustments → coerced to -1s / +1s.
        var anchor = new DateTimeOffset(2024, 6, 3, 5, 59, 0, TimeSpan.Zero);
        var zones = Build.Zones16();
        zones.Single(z => z.Id == 1).BoundToMaster1 = true; // bit 0
        zones.Single(z => z.Id == 2).BoundToMaster1 = true; // bit 1
        var masters = new[]
        {
            new MasterStation { Id = 1, MasterIndex = 1, ZoneId = 16, OnAdjustSeconds = 0, OffAdjustSeconds = 0 },
        };
        var data = Build.Data(
            Build.Settings(stationDelay: 10),
            zones,
            new[] { Build.FixedDaily(1, 360, (1, 60, 0), (2, 60, 1)) },
            masters);
        var runner = new DayRunner(data, anchor);

        runner.Run(220);

        // Master (bit 15) leads each zone by 1s, lags by 1s, and drops in the 10s station-delay gap.
        runner.Edges.Should().Equal(
            new Edge(61, 15, true),    // master leads zone 1 (-1 coercion)
            new Edge(62, 0, true),     // zone 1 on
            new Edge(122, 0, false),   // zone 1 off
            new Edge(124, 15, false),  // master lags zone 1 (+1 coercion), then drops in the gap
            new Edge(131, 15, true),   // master leads zone 2
            new Edge(132, 1, true),    // zone 2 on
            new Edge(192, 1, false),   // zone 2 off
            new Edge(194, 15, false)); // master lags zone 2
    }
}
