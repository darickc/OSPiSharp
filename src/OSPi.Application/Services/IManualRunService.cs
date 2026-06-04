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
}
