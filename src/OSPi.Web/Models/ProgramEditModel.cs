using System.ComponentModel.DataAnnotations;
using OSPi.Domain.Enums;
using ProgramEntity = OSPi.Domain.Entities.Program;
using ProgramStartTime = OSPi.Domain.Entities.ProgramStartTime;
using ProgramZoneDuration = OSPi.Domain.Entities.ProgramZoneDuration;

namespace OSPi.Web.Models;

/// <summary>
/// Editable, validation-bearing view model for the Program editor. Keeps the domain
/// <see cref="Program"/> POCO free of UI concerns and DataAnnotations. Map in via
/// <see cref="FromEntity"/> and out via <see cref="ToEntity"/>.
/// </summary>
public sealed class ProgramEditModel : IValidatableObject
{
    public const int MonthlyLastDayValue = 32;
    public const int MaxStartTimes = 4;

    public int Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(64, ErrorMessage = "Name must be 64 characters or fewer.")]
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool UseWeather { get; set; } = true;

    public OddEvenRestriction OddEven { get; set; } = OddEvenRestriction.None;

    public ScheduleType ScheduleType { get; set; } = ScheduleType.Weekly;

    // Weekly
    public byte WeekdayMask { get; set; }

    // Interval
    public int IntervalDays { get; set; } = 1;
    public int IntervalRemainder { get; set; }

    // Monthly (1..31, or "last day" toggle which persists as 32)
    public int MonthlyDay { get; set; } = 1;
    public bool MonthlyLastDay { get; set; }

    // Single run
    public DateOnly? SingleRunDate { get; set; }

    // Start times
    public StartTimeType StartTimeType { get; set; } = StartTimeType.Fixed;
    public int RepeatCount { get; set; }
    public int RepeatEveryMinutes { get; set; } = 30;

    // Date-range gate
    public bool DateRangeEnabled { get; set; }
    public int DateRangeStartMonth { get; set; } = 1;
    public int DateRangeStartDay { get; set; } = 1;
    public int DateRangeEndMonth { get; set; } = 12;
    public int DateRangeEndDay { get; set; } = 31;

    public List<StartTimeRow> StartTimes { get; set; } = new();

    public List<ZoneDurationRow> ZoneDurations { get; set; } = new();

    /// <summary>
    /// Moves a zone row from one position to another (the drag-and-drop reorder primitive).
    /// The list's visual order becomes the run order; <see cref="ToEntity"/> renumbers
    /// <see cref="ZoneDurationRow.RunOrder"/> from the resulting list position.
    /// </summary>
    public void MoveZone(int oldIndex, int newIndex)
    {
        if (oldIndex == newIndex
            || oldIndex < 0 || oldIndex >= ZoneDurations.Count
            || newIndex < 0 || newIndex >= ZoneDurations.Count)
        {
            return;
        }

        var row = ZoneDurations[oldIndex];
        ZoneDurations.RemoveAt(oldIndex);
        ZoneDurations.Insert(newIndex, row);
    }

    /// <summary>Sets the same duration on every currently-selected zone row (bulk edit).</summary>
    public void ApplyDurationToSelected(int durationSeconds)
    {
        foreach (var row in ZoneDurations.Where(r => r.Selected))
        {
            row.DurationSeconds = durationSeconds;
        }
    }

    public static ProgramEditModel NewProgram() => new()
    {
        StartTimes = { new StartTimeRow { Kind = StartTimeKind.FixedMinute, MinuteOfDay = 6 * 60 } },
    };

    public static ProgramEditModel FromEntity(ProgramEntity p)
    {
        var model = new ProgramEditModel
        {
            Id = p.Id,
            Name = p.Name,
            Enabled = p.Enabled,
            UseWeather = p.UseWeather,
            OddEven = p.OddEven,
            ScheduleType = p.ScheduleType,
            WeekdayMask = p.WeekdayMask,
            IntervalDays = p.IntervalDays,
            IntervalRemainder = p.IntervalRemainder,
            MonthlyLastDay = p.MonthlyDay == MonthlyLastDayValue,
            MonthlyDay = p.MonthlyDay == MonthlyLastDayValue ? 1 : Math.Clamp(p.MonthlyDay, 1, 31),
            SingleRunDate = p.SingleRunDate,
            StartTimeType = p.StartTimeType,
            RepeatCount = p.RepeatCount,
            RepeatEveryMinutes = p.RepeatEveryMinutes == 0 ? 30 : p.RepeatEveryMinutes,
            DateRangeEnabled = p.DateRangeEnabled,
            DateRangeStartMonth = p.DateRangeStartMonth == 0 ? 1 : p.DateRangeStartMonth,
            DateRangeStartDay = p.DateRangeStartDay == 0 ? 1 : p.DateRangeStartDay,
            DateRangeEndMonth = p.DateRangeEndMonth == 0 ? 12 : p.DateRangeEndMonth,
            DateRangeEndDay = p.DateRangeEndDay == 0 ? 31 : p.DateRangeEndDay,
            StartTimes = p.StartTimes
                .OrderBy(st => st.Slot)
                .Select(StartTimeRow.FromEntity)
                .ToList(),
            ZoneDurations = p.ZoneDurations
                .OrderBy(d => d.RunOrder)
                .Select(ZoneDurationRow.FromEntity)
                .ToList(),
        };

        if (model.StartTimes.Count == 0)
        {
            model.StartTimes.Add(new StartTimeRow { Kind = StartTimeKind.FixedMinute, MinuteOfDay = 6 * 60 });
        }

        return model;
    }

    public ProgramEntity ToEntity()
    {
        // In repeating mode only the anchor (first row) is meaningful.
        var startRows = StartTimeType == StartTimeType.Repeating
            ? StartTimes.Take(1).ToList()
            : StartTimes.Take(MaxStartTimes).ToList();

        var startTimes = startRows
            .Select((row, index) => row.ToEntity(index))
            .ToList();

        var durations = ZoneDurations
            .Select((row, index) => row.ToEntity(index))
            .ToList();

        return new ProgramEntity
        {
            Id = Id,
            Name = Name.Trim(),
            Enabled = Enabled,
            UseWeather = UseWeather,
            OddEven = OddEven,
            ScheduleType = ScheduleType,
            WeekdayMask = WeekdayMask,
            IntervalDays = IntervalDays,
            IntervalRemainder = IntervalRemainder,
            MonthlyDay = MonthlyLastDay ? MonthlyLastDayValue : MonthlyDay,
            SingleRunDate = SingleRunDate,
            StartTimeType = StartTimeType,
            RepeatCount = RepeatCount,
            RepeatEveryMinutes = RepeatEveryMinutes,
            DateRangeEnabled = DateRangeEnabled,
            DateRangeStartMonth = DateRangeStartMonth,
            DateRangeStartDay = DateRangeStartDay,
            DateRangeEndMonth = DateRangeEndMonth,
            DateRangeEndDay = DateRangeEndDay,
            StartTimes = startTimes,
            ZoneDurations = durations,
        };
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        switch (ScheduleType)
        {
            case ScheduleType.Weekly when WeekdayMask == 0:
                yield return new ValidationResult("Select at least one weekday.", new[] { nameof(WeekdayMask) });
                break;
            case ScheduleType.Interval when IntervalDays < 1:
                yield return new ValidationResult("Interval days must be at least 1.", new[] { nameof(IntervalDays) });
                break;
            case ScheduleType.Monthly when !MonthlyLastDay && MonthlyDay is < 1 or > 31:
                yield return new ValidationResult("Day of month must be between 1 and 31.", new[] { nameof(MonthlyDay) });
                break;
            case ScheduleType.SingleRun when SingleRunDate is null:
                yield return new ValidationResult("Choose a run date.", new[] { nameof(SingleRunDate) });
                break;
        }

        if (ScheduleType == ScheduleType.Interval && IntervalDays >= 1 &&
            (IntervalRemainder < 0 || IntervalRemainder >= IntervalDays))
        {
            yield return new ValidationResult(
                "Interval remainder must be between 0 and one less than the interval.",
                new[] { nameof(IntervalRemainder) });
        }

        if (StartTimeType == StartTimeType.Fixed)
        {
            if (StartTimes.Count == 0)
            {
                yield return new ValidationResult("Add at least one start time.", new[] { nameof(StartTimes) });
            }
            else if (StartTimes.Count > MaxStartTimes)
            {
                yield return new ValidationResult($"A program can have at most {MaxStartTimes} start times.", new[] { nameof(StartTimes) });
            }
        }
        else
        {
            if (StartTimes.Count == 0)
            {
                yield return new ValidationResult("Set the anchor start time.", new[] { nameof(StartTimes) });
            }

            if (RepeatCount > 0 && RepeatEveryMinutes < 1)
            {
                yield return new ValidationResult("Repeat interval must be at least 1 minute.", new[] { nameof(RepeatEveryMinutes) });
            }
        }

        if (ZoneDurations.Count == 0)
        {
            yield return new ValidationResult("Add at least one zone.", new[] { nameof(ZoneDurations) });
        }

        if (ZoneDurations.Select(d => d.ZoneId).Distinct().Count() != ZoneDurations.Count)
        {
            yield return new ValidationResult("Each zone may appear only once.", new[] { nameof(ZoneDurations) });
        }

        if (ZoneDurations.Any(d => d.DurationSeconds <= 0))
        {
            yield return new ValidationResult("Every zone duration must be greater than zero.", new[] { nameof(ZoneDurations) });
        }
    }

    public sealed class StartTimeRow
    {
        public StartTimeKind Kind { get; set; } = StartTimeKind.FixedMinute;

        /// <summary>Minute-of-day (0..1439) when <see cref="Kind"/> is FixedMinute.</summary>
        public int MinuteOfDay { get; set; } = 6 * 60;

        /// <summary>Signed minute offset when <see cref="Kind"/> is a sunrise/sunset kind.</summary>
        public int OffsetMinutes { get; set; }

        public static StartTimeRow FromEntity(ProgramStartTime st) => new()
        {
            Kind = st.Kind,
            MinuteOfDay = st.Kind == StartTimeKind.FixedMinute ? Math.Clamp(st.Value, 0, 1439) : 6 * 60,
            OffsetMinutes = st.Kind == StartTimeKind.FixedMinute ? 0 : st.Value,
        };

        public ProgramStartTime ToEntity(int slot) => new()
        {
            Slot = slot,
            Kind = Kind,
            Value = Kind == StartTimeKind.FixedMinute ? MinuteOfDay : OffsetMinutes,
        };
    }

    public sealed class ZoneDurationRow
    {
        public int ZoneId { get; set; }

        public int DurationSeconds { get; set; }

        public int RunOrder { get; set; }

        /// <summary>Transient multi-select state for the bulk-edit UI. Not persisted.</summary>
        public bool Selected { get; set; }

        public static ZoneDurationRow FromEntity(ProgramZoneDuration d) => new()
        {
            ZoneId = d.ZoneId,
            DurationSeconds = d.DurationSeconds,
            RunOrder = d.RunOrder,
        };

        // Id left at 0 — ProgramRepository.UpdateAsync merges by ZoneId, not Id.
        // RunOrder is the list position: drag-and-drop maintains the list in visual order,
        // so the index passed by ProgramEditModel.ToEntity is the authoritative run order.
        public ProgramZoneDuration ToEntity(int index) => new()
        {
            ZoneId = ZoneId,
            DurationSeconds = DurationSeconds,
            RunOrder = index,
        };
    }
}
