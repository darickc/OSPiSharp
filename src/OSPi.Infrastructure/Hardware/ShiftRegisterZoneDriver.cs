using System.Device.Gpio;
using Microsoft.Extensions.Logging;
using OSPi.Application.Hardware;

namespace OSPi.Infrastructure.Hardware;

/// <summary>
/// Drives daisy-chained 74HC595 shift registers on a Raspberry Pi via System.Device.Gpio.
/// Direct port of the C++ firmware's apply_all_station_bits(): latch low, then for each
/// board from highest to lowest shift out 8 bits MSB-first (set data, pulse clock), then
/// latch high to present all outputs atomically. Output-enable is driven low once at
/// startup to enable the register outputs.
/// </summary>
public sealed class ShiftRegisterZoneDriver : IZoneDriver, IDisposable
{
    private readonly ShiftRegisterOptions _opts;
    private readonly ILogger<ShiftRegisterZoneDriver> _logger;
    private readonly GpioController _gpio;

    public ShiftRegisterZoneDriver(int zoneCount, ShiftRegisterOptions opts, ILogger<ShiftRegisterZoneDriver> logger)
    {
        ZoneCount = zoneCount;
        _opts = opts;
        _logger = logger;

        _gpio = new GpioController();
        _gpio.OpenPin(_opts.LatchPin, PinMode.Output, PinValue.Low);
        _gpio.OpenPin(_opts.ClockPin, PinMode.Output, PinValue.Low);
        _gpio.OpenPin(_opts.DataPin, PinMode.Output, PinValue.Low);
        _gpio.OpenPin(_opts.OutputEnablePin, PinMode.Output, PinValue.High); // disabled until first Apply

        // Start with everything off, then enable outputs.
        Apply(new bool[zoneCount]);
        _gpio.Write(_opts.OutputEnablePin, PinValue.Low); // active-low OE: low = outputs enabled

        _logger.LogInformation(
            "ShiftRegisterZoneDriver initialized: {Zones} zones, {Boards} boards (latch={Latch}, clock={Clock}, data={Data}, oe={Oe}).",
            zoneCount, _opts.BoardCount, _opts.LatchPin, _opts.ClockPin, _opts.DataPin, _opts.OutputEnablePin);
    }

    public int ZoneCount { get; }

    public void Apply(ReadOnlySpan<bool> zoneStates)
    {
        if (zoneStates.Length != ZoneCount)
            throw new ArgumentException($"Expected {ZoneCount} zone states, got {zoneStates.Length}.", nameof(zoneStates));

        _gpio.Write(_opts.LatchPin, PinValue.Low);

        // Highest board first so that after the full chain shifts, board 0 ends up nearest
        // the latch outputs — matching the firmware's ordering.
        for (var board = _opts.BoardCount - 1; board >= 0; board--)
        {
            for (var bit = 7; bit >= 0; bit--) // MSB-first within each board
            {
                _gpio.Write(_opts.ClockPin, PinValue.Low);

                var zoneId = board * 8 + bit;
                var on = zoneId < ZoneCount && zoneStates[zoneId];
                var level = (on ^ _opts.ActiveLow) ? PinValue.High : PinValue.Low;
                _gpio.Write(_opts.DataPin, level);

                _gpio.Write(_opts.ClockPin, PinValue.High);
            }
        }

        _gpio.Write(_opts.LatchPin, PinValue.High);
    }

    public void Dispose()
    {
        // Best-effort: disable outputs and turn everything off on shutdown.
        try
        {
            _gpio.Write(_opts.OutputEnablePin, PinValue.High);
            Apply(new bool[ZoneCount]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during ShiftRegisterZoneDriver shutdown.");
        }

        _gpio.Dispose();
    }
}
