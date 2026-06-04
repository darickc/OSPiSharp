namespace OSPi.Application.Services;

/// <summary>
/// The single entry point for manual zone control. Both the Blazor UI and the MCP server
/// call this; it translates intent into engine commands. Keeping one path means UI clicks,
/// map taps, and natural-language commands all behave identically.
/// </summary>
public interface IManualRunService
{
    /// <summary>Turn a single zone on.</summary>
    void TurnOn(int zoneId);

    /// <summary>Turn a single zone off.</summary>
    void TurnOff(int zoneId);

    /// <summary>Toggle a single zone based on its current desired state.</summary>
    void Toggle(int zoneId, bool on);

    /// <summary>Turn every zone off immediately.</summary>
    void StopAll();

    /// <summary>Start a program now (insert-front), ignoring its calendar match.</summary>
    void RunProgram(int programId);

    /// <summary>
    /// Run a single zone for a fixed number of seconds (insert-front). The identifier is the
    /// zone's <em>hardware bit</em> (matching <c>ZoneStatus.ZoneId</c>), not its entity id.
    /// </summary>
    void RunZoneTimed(int hardwareBit, int seconds);

    /// <summary>
    /// Stop a single zone immediately, identified by its <em>hardware bit</em> (the counterpart to
    /// <see cref="RunZoneTimed"/>). Cancels a running timed or program run by dropping its queue
    /// item and clears any indefinite manual override; unlike <see cref="TurnOff"/>, a queued run
    /// does not re-assert it on the next tick.
    /// </summary>
    void StopZone(int hardwareBit);

    /// <summary>Start a rain delay for the given minutes, or clear it when <paramref name="minutes"/> ≤ 0.</summary>
    void SetRainDelay(int minutes);

    /// <summary>Suppress hardware output for the given seconds while the queue keeps advancing.</summary>
    void Pause(int seconds);

    /// <summary>Resume hardware output after a <see cref="Pause"/>.</summary>
    void Resume();
}
