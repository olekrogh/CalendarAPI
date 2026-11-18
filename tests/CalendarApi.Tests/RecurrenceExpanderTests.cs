using CalendarApi.Models;
using CalendarApi.Services;

namespace CalendarApi.Tests;

/// <summary>Unit tests for the recurrence engine (daily and weekly series).</summary>
public class RecurrenceExpanderTests
{
    // 2026-01-05 is a Monday.
    private static readonly DateTimeOffset Monday = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    private static CalendarEvent Event(RecurrenceRule? rule, DateTimeOffset? start = null, TimeSpan? length = null)
    {
        var s = start ?? Monday;
        return new CalendarEvent(Guid.NewGuid(), Guid.NewGuid(), "Stand-up", null, s, s + (length ?? TimeSpan.FromHours(1)), rule);
    }

    private static List<EventOccurrence> Expand(CalendarEvent ev, DateTimeOffset from, DateTimeOffset to) =>
        RecurrenceExpander.Expand(ev, from, to).ToList();

    [Fact]
    public void NonRecurring_InsideWindow_ReturnsSingleOccurrence()
    {
        var result = Expand(Event(null), Monday.AddDays(-1), Monday.AddDays(1));

        var occurrence = Assert.Single(result);
        Assert.False(occurrence.IsRecurring);
        Assert.Equal(Monday, occurrence.Start);
    }

    [Fact]
    public void NonRecurring_OutsideWindow_ReturnsNothing() =>
        Assert.Empty(Expand(Event(null), Monday.AddDays(1), Monday.AddDays(2)));

    [Fact]
    public void Occurrence_OverlappingWindowStart_IsIncluded()
    {
        // Event runs 09:00-10:00; window starts at 09:30.
        var result = Expand(Event(null), Monday.AddMinutes(30), Monday.AddDays(1));
        Assert.Single(result);
    }

    [Fact]
    public void Occurrence_EndingExactlyAtWindowStart_IsExcluded() =>
        Assert.Empty(Expand(Event(null), Monday.AddHours(1), Monday.AddDays(1)));

    [Fact]
    public void Daily_EveryDay_ProducesOneOccurrencePerDay()
    {
        var ev = Event(new RecurrenceRule(RecurrenceFrequency.Daily));

        var result = Expand(ev, Monday, Monday.AddDays(5));

        Assert.Equal(5, result.Count);
        Assert.All(result, o => Assert.True(o.IsRecurring));
        Assert.Equal(Enumerable.Range(0, 5).Select(i => Monday.AddDays(i)), result.Select(o => o.Start));
    }

    [Fact]
    public void Daily_Interval_SkipsDays()
    {
        var ev = Event(new RecurrenceRule(RecurrenceFrequency.Daily, Interval: 3));

        var result = Expand(ev, Monday, Monday.AddDays(10));

        Assert.Equal([0, 3, 6, 9], result.Select(o => (int)(o.Start - Monday).TotalDays));
    }

    [Fact]
    public void Daily_WithCount_StopsAfterCount()
    {
        var ev = Event(new RecurrenceRule(RecurrenceFrequency.Daily, Count: 3));

        Assert.Equal(3, Expand(ev, Monday, Monday.AddYears(1)).Count);
    }

    [Fact]
    public void Daily_WithUntil_IsInclusive()
    {
        var ev = Event(new RecurrenceRule(RecurrenceFrequency.Daily, Until: Monday.AddDays(2)));

        Assert.Equal(3, Expand(ev, Monday, Monday.AddYears(1)).Count);
    }

    [Fact]
    public void Daily_PreservesDuration()
    {
        var ev = Event(new RecurrenceRule(RecurrenceFrequency.Daily, Count: 2), length: TimeSpan.FromMinutes(90));

        Assert.All(Expand(ev, Monday, Monday.AddDays(5)), o => Assert.Equal(TimeSpan.FromMinutes(90), o.End - o.Start));
    }

    [Fact]
    public void Daily_WindowLaterThanStart_OnlyReturnsOccurrencesInWindow()
    {
        var ev = Event(new RecurrenceRule(RecurrenceFrequency.Daily));

        var result = Expand(ev, Monday.AddDays(10), Monday.AddDays(12));

        Assert.Equal([Monday.AddDays(10), Monday.AddDays(11)], result.Select(o => o.Start));
    }

    [Fact]
    public void Weekly_WithoutDays_RepeatsOnStartWeekday()
    {
        var ev = Event(new RecurrenceRule(RecurrenceFrequency.Weekly));

        var result = Expand(ev, Monday, Monday.AddDays(21));

        Assert.Equal([Monday, Monday.AddDays(7), Monday.AddDays(14)], result.Select(o => o.Start));
    }

    [Fact]
    public void Weekly_MultipleDays_ProducesEachDayInOrder()
    {
        var ev = Event(new RecurrenceRule(RecurrenceFrequency.Weekly,
            DaysOfWeek: [DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Wednesday]));

        var result = Expand(ev, Monday, Monday.AddDays(14));

        Assert.Equal(
            [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
            result.Select(o => o.Start.DayOfWeek));
    }

    [Fact]
    public void Weekly_Interval_SkipsWeeks()
    {
        var ev = Event(new RecurrenceRule(RecurrenceFrequency.Weekly, Interval: 2));

        var result = Expand(ev, Monday, Monday.AddDays(35));

        Assert.Equal([Monday, Monday.AddDays(14), Monday.AddDays(28)], result.Select(o => o.Start));
    }

    [Fact]
    public void Weekly_StartMidWeek_IgnoresEarlierDaysOfFirstWeek()
    {
        // Series starts on Wednesday but lists Monday too: the Monday before the start must not appear.
        var wednesday = Monday.AddDays(2);
        var ev = Event(new RecurrenceRule(RecurrenceFrequency.Weekly,
            DaysOfWeek: [DayOfWeek.Monday, DayOfWeek.Wednesday]), start: wednesday);

        var result = Expand(ev, Monday, Monday.AddDays(14));

        Assert.Equal([wednesday, Monday.AddDays(7), wednesday.AddDays(7)], result.Select(o => o.Start));
    }

    [Fact]
    public void Weekly_SundayStart_BelongsToWeekEndingThatSunday()
    {
        var sunday = Monday.AddDays(6);
        var ev = Event(new RecurrenceRule(RecurrenceFrequency.Weekly, Interval: 2,
            DaysOfWeek: [DayOfWeek.Monday, DayOfWeek.Sunday]), start: sunday);

        var result = Expand(ev, sunday, sunday.AddDays(15));

        // Sunday (week 0), then the next series week is two weeks later: Monday and Sunday of that week.
        Assert.Equal([sunday, Monday.AddDays(14), sunday.AddDays(14)], result.Select(o => o.Start));
    }

    [Fact]
    public void Weekly_WithCount_CountsAcrossDays()
    {
        var ev = Event(new RecurrenceRule(RecurrenceFrequency.Weekly, Count: 5,
            DaysOfWeek: [DayOfWeek.Monday, DayOfWeek.Thursday]));

        Assert.Equal(5, Expand(ev, Monday, Monday.AddYears(1)).Count);
    }

    [Fact]
    public void Weekly_WithUntil_StopsAtUntil()
    {
        var ev = Event(new RecurrenceRule(RecurrenceFrequency.Weekly, Until: Monday.AddDays(14)));

        Assert.Equal(3, Expand(ev, Monday, Monday.AddYears(1)).Count);
    }

    [Fact]
    public void Series_ExpandsOnlyWithinWindow_EvenWhenInfinite()
    {
        var ev = Event(new RecurrenceRule(RecurrenceFrequency.Daily));

        // Terminates despite no Count/Until because the window bounds the enumeration.
        Assert.Equal(7, Expand(ev, Monday.AddYears(5), Monday.AddYears(5).AddDays(7)).Count);
    }

    [Fact]
    public void ExtremeInterval_NearMaxDate_DoesNotThrow()
    {
        var late = new DateTimeOffset(9999, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var ev = Event(new RecurrenceRule(RecurrenceFrequency.Daily, Interval: 1000), start: late);

        var result = Expand(ev, late, late.AddDays(30));

        Assert.Single(result);
    }
}
