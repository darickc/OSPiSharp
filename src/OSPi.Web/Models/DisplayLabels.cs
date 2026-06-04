using OSPi.Domain.Enums;

namespace OSPi.Web.Models;

/// <summary>Friendly display text for domain enums, shared across the config screens.</summary>
public static class DisplayLabels
{
    public static string ZoneGroup(ZoneGroup group) => group switch
    {
        Domain.Enums.ZoneGroup.Sequential0 => "Sequential 0",
        Domain.Enums.ZoneGroup.Sequential1 => "Sequential 1",
        Domain.Enums.ZoneGroup.Sequential2 => "Sequential 2",
        Domain.Enums.ZoneGroup.Sequential3 => "Sequential 3",
        Domain.Enums.ZoneGroup.Parallel => "Parallel",
        Domain.Enums.ZoneGroup.Independent => "Independent",
        _ => group.ToString(),
    };

    public static string ScheduleType(ScheduleType type) => type switch
    {
        Domain.Enums.ScheduleType.Weekly => "Weekly",
        Domain.Enums.ScheduleType.SingleRun => "Single run",
        Domain.Enums.ScheduleType.Monthly => "Monthly",
        Domain.Enums.ScheduleType.Interval => "Interval",
        _ => type.ToString(),
    };

    public static string OddEven(OddEvenRestriction restriction) => restriction switch
    {
        OddEvenRestriction.None => "No restriction",
        OddEvenRestriction.OddDays => "Odd days",
        OddEvenRestriction.EvenDays => "Even days",
        _ => restriction.ToString(),
    };

    public static string StartTimeKind(StartTimeKind kind) => kind switch
    {
        Domain.Enums.StartTimeKind.FixedMinute => "Fixed time",
        Domain.Enums.StartTimeKind.SunriseOffset => "Sunrise +/- offset",
        Domain.Enums.StartTimeKind.SunsetOffset => "Sunset +/- offset",
        _ => kind.ToString(),
    };
}
