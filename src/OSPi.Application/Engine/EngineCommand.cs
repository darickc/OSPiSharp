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

    /// <summary>Turn every zone off immediately.</summary>
    public sealed record StopAll : EngineCommand;
}
