using System.ComponentModel;
using ModelContextProtocol.Server;
using OSPi.Application.Engine;
using OSPi.Application.Persistence;
using OSPi.Application.Services;

namespace OSPi.Mcp;

/// <summary>
/// MCP tool surface for natural-language control of the sprinkler controller. These are thin,
/// stateless wrappers over the same application services the Blazor UI uses, so AI-driven runs
/// behave identically to button clicks. Hosted in-process by OSPi.Web (one engine, one DbContext).
/// </summary>
/// <remarks>
/// Zones are exposed to the AI as <em>zone numbers</em> 1..16 (matching the dashboard's "Zone N"
/// labels), which equal <c>HardwareBit + 1</c>. The methods translate to the identifier each
/// service actually needs: <see cref="IManualRunService.RunZoneTimed"/> takes the hardware bit,
/// while <see cref="IManualRunService.TurnOff"/> takes the entity <c>Zone.Id</c>.
/// </remarks>
[McpServerToolType]
public static class SprinklerTools
{
    /// <summary>Lowest valid zone number exposed to the AI.</summary>
    public const int MinZoneNumber = 1;

    /// <summary>Highest valid zone number exposed to the AI (16-zone OSPi clone).</summary>
    public const int MaxZoneNumber = 16;

    [McpServerTool(Name = "list_zones")]
    [Description("List all sprinkler zones with their number (1-16), name, sequencing group, and master bindings.")]
    public static async Task<IReadOnlyList<ZoneInfo>> ListZonesAsync(
        IZoneRepository zones,
        CancellationToken ct = default)
    {
        var all = await zones.GetAllAsync(ct);
        return all
            .OrderBy(z => z.HardwareBit)
            .Select(z => new ZoneInfo(
                z.HardwareBit + 1,
                string.IsNullOrWhiteSpace(z.Name) ? $"Zone {z.HardwareBit + 1}" : z.Name,
                z.Group.ToString(),
                z.Disabled,
                z.BoundToMaster1,
                z.BoundToMaster2))
            .ToList();
    }

    [McpServerTool(Name = "list_programs")]
    [Description("List all watering programs with their id, name, whether they are enabled, and schedule type.")]
    public static async Task<IReadOnlyList<ProgramInfo>> ListProgramsAsync(
        IProgramRepository programs,
        CancellationToken ct = default)
    {
        var all = await programs.GetAllAsync(ct);
        return all
            .Select(p => new ProgramInfo(p.Id, p.Name, p.Enabled, p.ScheduleType.ToString()))
            .ToList();
    }

    [McpServerTool(Name = "get_status")]
    [Description("Get the current live status: system enabled, paused, rain-delay expiry, water-level percent, and per-zone on/queued state with seconds remaining.")]
    public static StatusInfo GetStatus(IStateHub stateHub)
        => StatusInfo.From(stateHub.Latest);

    [McpServerTool(Name = "run_zone")]
    [Description("Start a single zone now for a given number of minutes. Returns a confirmation.")]
    public static CommandResult RunZone(
        [Description("Zone number, 1-16.")] int zoneNumber,
        [Description("How many minutes to run, greater than 0.")] int minutes,
        IManualRunService manualRun)
    {
        ValidateZoneNumber(zoneNumber);
        if (minutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "Minutes must be greater than 0.");
        }

        manualRun.RunZoneTimed(zoneNumber - 1, minutes * 60);
        return CommandResult.Success($"Zone {zoneNumber} running for {minutes} min.");
    }

    [McpServerTool(Name = "stop_zone")]
    [Description("Stop a single running zone immediately (cancels a timed or program run). Returns a confirmation.")]
    public static CommandResult StopZone(
        [Description("Zone number, 1-16.")] int zoneNumber,
        IManualRunService manualRun)
    {
        ValidateZoneNumber(zoneNumber);
        manualRun.StopZone(zoneNumber - 1);
        return CommandResult.Success($"Zone {zoneNumber} stopped.");
    }

    [McpServerTool(Name = "stop_all")]
    [Description("Turn every zone off immediately. Returns a confirmation.")]
    public static CommandResult StopAll(IManualRunService manualRun)
    {
        manualRun.StopAll();
        return CommandResult.Success("All zones stopped.");
    }

    [McpServerTool(Name = "run_program")]
    [Description("Start a watering program now, ignoring its calendar. Use list_programs to find the program id. Returns a confirmation.")]
    public static async Task<CommandResult> RunProgramAsync(
        [Description("The program id (see list_programs).")] int programId,
        IManualRunService manualRun,
        IProgramRepository programs,
        CancellationToken ct = default)
    {
        var all = await programs.GetAllAsync(ct);
        var program = all.FirstOrDefault(p => p.Id == programId);
        if (program is null)
        {
            throw new ArgumentException($"No program with id {programId}.", nameof(programId));
        }

        manualRun.RunProgram(programId);
        return CommandResult.Success($"Program '{program.Name}' started.");
    }

    [McpServerTool(Name = "set_rain_delay")]
    [Description("Start a rain delay for the given number of minutes, or clear it with 0 (or a negative value). Returns a confirmation.")]
    public static CommandResult SetRainDelay(
        [Description("Rain-delay duration in minutes; 0 or negative clears any active delay.")] int minutes,
        IManualRunService manualRun)
    {
        manualRun.SetRainDelay(minutes);
        return minutes > 0
            ? CommandResult.Success($"Rain delay set for {minutes} min.")
            : CommandResult.Success("Rain delay cleared.");
    }

    private static void ValidateZoneNumber(int zoneNumber)
    {
        if (zoneNumber is < MinZoneNumber or > MaxZoneNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(zoneNumber), zoneNumber, $"Zone number must be between {MinZoneNumber} and {MaxZoneNumber}.");
        }
    }
}

/// <summary>A sprinkler zone as seen by the AI (number is 1-based, = HardwareBit + 1).</summary>
public sealed record ZoneInfo(
    int Number,
    string Name,
    string Group,
    bool Disabled,
    bool BoundToMaster1,
    bool BoundToMaster2);

/// <summary>A watering program in list form.</summary>
public sealed record ProgramInfo(int Id, string Name, bool Enabled, string ScheduleType);

/// <summary>Live controller status projected for the AI.</summary>
public sealed record StatusInfo(
    bool EngineReady,
    bool SystemEnabled,
    bool Paused,
    DateTimeOffset? RainDelayUntil,
    int WaterLevelPercent,
    IReadOnlyList<ZoneState> Zones)
{
    /// <summary>Projects an engine snapshot (or its absence) into the AI-facing shape.</summary>
    public static StatusInfo From(StatusSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            // The engine hasn't published its first per-second tick yet.
            return new StatusInfo(false, true, false, null, 100, Array.Empty<ZoneState>());
        }

        var zones = snapshot.Zones
            .OrderBy(z => z.ZoneId)
            .Select(z => new ZoneState(z.ZoneId + 1, z.On, z.SecondsRemaining, z.Queued, z.ProgramId))
            .ToList();

        return new StatusInfo(
            true,
            snapshot.SystemEnabled,
            snapshot.Paused,
            snapshot.RainDelayUntil,
            snapshot.WaterLevelPercent,
            zones);
    }
}

/// <summary>Per-zone live state (number is 1-based, = hardware bit + 1).</summary>
public sealed record ZoneState(int Number, bool On, int? SecondsRemaining, bool Queued, int? ProgramId);

/// <summary>Uniform confirmation returned by the mutating tools.</summary>
public sealed record CommandResult(bool Ok, string Message)
{
    public static CommandResult Success(string message) => new(true, message);
}
