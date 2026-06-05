namespace OSPi.Application.Engine;

/// <summary>
/// Commands posted into the engine's channel by services (UI, MCP). The engine is the
/// single owner of mutable runtime state and the only writer to hardware; everything that
/// wants to change state posts a command that the engine drains at the top of each tick.
/// </summary>
public abstract record EngineCommand
{
    /// <summary>Phase 0: directly force a single zone on or off (manual control).</summary>
    public sealed record SetZone(int ZoneId, bool On) : EngineCommand;

    /// <summary>Turn every zone off immediately and clear the runtime queue.</summary>
    public sealed record StopAll : EngineCommand;

    /// <summary>
    /// Manually start a program now, ignoring its calendar match. Its per-zone durations
    /// (water-level scaled) are appended to the queue behind any currently running or
    /// queued zones in the same sequential group, rather than preempting them.
    /// </summary>
    public sealed record RunProgram(int ProgramId) : EngineCommand;

    /// <summary>
    /// Manually run a single zone for a fixed number of seconds, appended behind any
    /// running or queued zones in its sequential group.
    /// </summary>
    public sealed record RunZoneTimed(int ZoneId, int Seconds) : EngineCommand;

    /// <summary>
    /// Stop a single zone: drop its runtime-queue item (cancelling a timed/program run) and clear
    /// any indefinite manual override, identified by <em>hardware bit</em> like
    /// <see cref="RunZoneTimed"/>. The per-second pass closes the run-log entry on the next tick.
    /// </summary>
    public sealed record CancelZone(int HardwareBit) : EngineCommand;

    /// <summary>
    /// Start (or, with <c>Minutes &lt;= 0</c>, clear) a rain delay. While active, scheduled
    /// matches are suppressed for zones that do not ignore rain, and such queued/running
    /// zones are stopped.
    /// </summary>
    public sealed record SetRainDelay(int Minutes) : EngineCommand;

    /// <summary>Pause hardware output for the given number of seconds (queue keeps advancing).</summary>
    public sealed record Pause(int Seconds) : EngineCommand;

    /// <summary>Resume hardware output after a <see cref="Pause"/>.</summary>
    public sealed record Resume : EngineCommand;

    /// <summary>Reload the cached scheduling data from persistence (after a config edit).</summary>
    public sealed record ReloadConfig : EngineCommand;
}
