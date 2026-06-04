namespace OSPi.Domain.Enums;

/// <summary>
/// Whether a program uses explicit fixed start times or a repeating schedule.
/// Mirrors the firmware <c>ProgramStruct.starttime_type</c> field.
/// </summary>
public enum StartTimeType
{
    /// <summary>
    /// A single anchor start (slot 0) repeated <c>RepeatCount</c> times every
    /// <c>RepeatEveryMinutes</c> minutes.
    /// </summary>
    Repeating = 0,

    /// <summary>Up to four explicit start times.</summary>
    Fixed = 1,
}
