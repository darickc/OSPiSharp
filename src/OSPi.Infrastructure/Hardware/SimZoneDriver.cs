using Microsoft.Extensions.Logging;
using OSPi.Application.Hardware;

namespace OSPi.Infrastructure.Hardware;

/// <summary>
/// In-memory zone driver for development on a non-Pi machine. Tracks state, logs every
/// transition, and raises <see cref="StateChanged"/> so a dev "virtual board" UI can watch
/// zones flip. Selected when Hardware:Driver is "Sim".
/// </summary>
public sealed class SimZoneDriver : IZoneDriver
{
    private readonly ILogger<SimZoneDriver> _logger;
    private readonly bool[] _state;

    public SimZoneDriver(int zoneCount, ILogger<SimZoneDriver> logger)
    {
        ZoneCount = zoneCount;
        _state = new bool[zoneCount];
        _logger = logger;
    }

    public int ZoneCount { get; }

    /// <summary>Raised after each <see cref="Apply"/> that changes any zone.</summary>
    public event Action<IReadOnlyList<bool>>? StateChanged;

    public void Apply(ReadOnlySpan<bool> zoneStates)
    {
        if (zoneStates.Length != ZoneCount)
            throw new ArgumentException($"Expected {ZoneCount} zone states, got {zoneStates.Length}.", nameof(zoneStates));

        var changed = false;
        for (var i = 0; i < ZoneCount; i++)
        {
            if (_state[i] == zoneStates[i]) continue;
            _state[i] = zoneStates[i];
            changed = true;
            _logger.LogInformation("[SIM] Zone {ZoneId} -> {State}", i, zoneStates[i] ? "ON" : "OFF");
        }

        if (changed)
            StateChanged?.Invoke(_state.AsReadOnly());
    }
}

internal static class ArrayExtensions
{
    public static IReadOnlyList<T> AsReadOnly<T>(this T[] array) => Array.AsReadOnly(array);
}
