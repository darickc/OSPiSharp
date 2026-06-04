namespace OSPi.Domain.Entities;

/// <summary>
/// A completed zone run, recorded for history (firmware <c>LogStruct</c>). Written by
/// the engine starting in Phase 3; the table and read queries exist now as a target.
/// </summary>
public sealed class RunLogEntry
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Zone that ran.</summary>
    public int ZoneId { get; set; }

    /// <summary>Zone navigation.</summary>
    public Zone Zone { get; set; } = null!;

    /// <summary>Program that triggered the run, or null for a manual run.</summary>
    public int? ProgramId { get; set; }

    /// <summary>Program navigation (null for manual runs).</summary>
    public Program? Program { get; set; }

    /// <summary>When the run started.</summary>
    public DateTimeOffset StartTime { get; set; }

    /// <summary>When the run ended.</summary>
    public DateTimeOffset EndTime { get; set; }

    /// <summary>Actual run duration in seconds.</summary>
    public int DurationSeconds { get; set; }
}
