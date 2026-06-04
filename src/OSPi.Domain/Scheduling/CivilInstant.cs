namespace OSPi.Domain.Scheduling;

/// <summary>
/// A point in <em>site-local civil time</em> — the time basis all day/time matching runs on.
///
/// <para>This mirrors the firmware's time model exactly. The firmware computes
/// <c>curr_time = now_tz()</c> (OpenSprinkler.cpp:485-487), a UTC epoch shifted by a
/// <em>fixed</em> timezone offset with <strong>no DST transitions</strong>, then calls
/// <c>gmtime()</c> on it (program.cpp:237) so every weekday/day/month/minute field is a
/// local wall-clock value. We deliberately match this — honoring real DST would diverge
/// from the oracle — so the pure scheduling functions take a <see cref="CivilInstant"/>
/// and never see UTC. The engine (a later phase) builds one as
/// <c>clock.GetUtcNow() + siteOffset</c>.</para>
/// </summary>
public readonly record struct CivilInstant(DateTime LocalDateTime)
{
    private static readonly DateOnly UnixEpoch = new(1970, 1, 1);

    /// <summary>Minute within the day (0..1439), mirroring <c>(t%86400)/60</c> (program.cpp:323).</summary>
    public int MinuteOfDay => (LocalDateTime.Hour * 60) + LocalDateTime.Minute;

    /// <summary>The civil calendar date.</summary>
    public DateOnly Date => DateOnly.FromDateTime(LocalDateTime);

    /// <summary>
    /// Whole days since 1970-01-01 in civil time, reproducing the firmware's
    /// <c>t/86400</c> (program.cpp:244, 291) without round-tripping through a Unix
    /// timestamp. Used by interval scheduling's modulus.
    /// </summary>
    public int EpochDay => Date.DayNumber - UnixEpoch.DayNumber;

    /// <summary>Day of week (.NET semantics: Sunday = 0).</summary>
    public DayOfWeek DayOfWeek => LocalDateTime.DayOfWeek;

    /// <summary>The same instant one civil day earlier, preserving the minute-of-day.</summary>
    public CivilInstant PreviousDay() => new(LocalDateTime.AddDays(-1));
}
