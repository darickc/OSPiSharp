namespace OSPi.Domain.Entities;

/// <summary>
/// Join entity: how long a zone runs within a program, and in what order. Replaces
/// the firmware's per-station duration array. <see cref="RunOrder"/> is first-class
/// here; the drag-and-drop editor that sets it arrives in Phase 4.
/// </summary>
public sealed class ProgramZoneDuration
{
    /// <summary>Surrogate primary key (friendlier for UI keying and reordering than a composite).</summary>
    public int Id { get; set; }

    /// <summary>Owning program.</summary>
    public int ProgramId { get; set; }

    /// <summary>Owning program navigation.</summary>
    public Program Program { get; set; } = null!;

    /// <summary>Target zone.</summary>
    public int ZoneId { get; set; }

    /// <summary>Target zone navigation.</summary>
    public Zone Zone { get; set; } = null!;

    /// <summary>Run duration in seconds.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>Enqueue order within a sequential group (lower runs first).</summary>
    public int RunOrder { get; set; }
}
