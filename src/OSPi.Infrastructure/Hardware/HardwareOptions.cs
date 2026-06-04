namespace OSPi.Infrastructure.Hardware;

/// <summary>Bound from the "Hardware" configuration section.</summary>
public sealed class HardwareOptions
{
    public const string SectionName = "Hardware";

    /// <summary>"Sim" (default, runs anywhere) or "ShiftRegister" (real OSPi hardware).</summary>
    public string Driver { get; set; } = "Sim";

    /// <summary>Total number of zones. OSPi-clone with two 74HC595s = 16.</summary>
    public int ZoneCount { get; set; } = 16;

    public ShiftRegisterOptions ShiftRegister { get; set; } = new();
}

/// <summary>
/// BCM GPIO pin numbers for the daisy-chained 74HC595 shift registers, mirroring the C++
/// firmware's PIN_SR_* definitions. Defaults match the OSPi pin map.
/// </summary>
public sealed class ShiftRegisterOptions
{
    public int LatchPin { get; set; } = 22;
    public int ClockPin { get; set; } = 4;
    public int DataPin { get; set; } = 27;
    public int OutputEnablePin { get; set; } = 17;

    /// <summary>Number of 8-bit shift-register boards in the chain (16 zones = 2).</summary>
    public int BoardCount { get; set; } = 2;

    /// <summary>
    /// True if a solenoid is energized by a LOW output (active-low wiring). Default false
    /// (active-high), matching standard OSPi shift-register outputs.
    /// </summary>
    public bool ActiveLow { get; set; }
}
