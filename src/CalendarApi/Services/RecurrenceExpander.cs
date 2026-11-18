using CalendarApi.Models;

namespace CalendarApi.Services;

/// <summary>
/// Pure logic that turns an event (optionally recurring) into concrete occurrences.
/// Occurrences keep the wall-clock offset of the original start (no DST adjustment).
/// </summary>
public static class RecurrenceExpander
{
    /// <summary>Safety cap so a huge query window can never return an unbounded response.</summary>
    public const int MaxOccurrences = 5000;

    /// <summary>
    /// Returns every occurrence that overlaps the half-open window [from, to), ordered by start.
    /// </summary>
    public static IEnumerable<EventOccurrence> Expand(CalendarEvent ev, DateTimeOffset from, DateTimeOffset to)
    {
        var duration = ev.End - ev.Start;
        var isRecurring = ev.Recurrence is not null;

        foreach (var start in Starts(ev))
        {
            // Starts are ascending, so once we pass the window we can stop (this also bounds infinite series).
            if (start >= to)
                yield break;

            var end = start + duration;
            if (end > from)
                yield return new EventOccurrence(ev.Id, ev.CalendarId, ev.Title, ev.Description, start, end, isRecurring, ev.ResourceId);
        }
    }

    /// <summary>Lazily yields occurrence start times in ascending order, honouring Until and Count.</summary>
    private static IEnumerable<DateTimeOffset> Starts(CalendarEvent ev)
    {
        var rule = ev.Recurrence;
        if (rule is null)
        {
            yield return ev.Start;
            yield break;
        }

        var emitted = 0;
        foreach (var start in RawStarts(ev.Start, rule))
        {
            if (rule.Until is { } until && start > until)
                yield break;
            if (rule.Count is { } count && emitted >= count)
                yield break;

            emitted++;
            yield return start;
        }
    }

    /// <summary>Unbounded (until the date range overflows) sequence of candidate starts.</summary>
    private static IEnumerable<DateTimeOffset> RawStarts(DateTimeOffset first, RecurrenceRule rule)
    {
        var interval = Math.Max(1, rule.Interval);

        if (rule.Frequency == RecurrenceFrequency.Daily)
        {
            for (long i = 0; ; i++)
            {
                if (!TryAddDays(first, i * interval, out var next))
                    yield break;
                yield return next;
            }
        }

        // Weekly: weeks run Monday..Sunday and are counted from the week containing the first start.
        var days = (rule.DaysOfWeek is { Count: > 0 } d ? d : [first.DayOfWeek])
            .Distinct()
            .OrderBy(DayIndex)
            .ToArray();

        // Move back to the Monday of the first week, keeping the time of day.
        if (!TryAddDays(first, -DayIndex(first.DayOfWeek), out var anchor))
            yield break;

        for (long week = 0; ; week++)
        {
            foreach (var day in days)
            {
                if (!TryAddDays(anchor, week * interval * 7 + DayIndex(day), out var candidate))
                    yield break;

                // Days earlier in the first week than the actual start are not part of the series.
                if (candidate < first)
                    continue;

                yield return candidate;
            }
        }
    }

    /// <summary>Monday = 0 ... Sunday = 6.</summary>
    private static int DayIndex(DayOfWeek day) => ((int)day + 6) % 7;

    /// <summary>AddDays that reports overflow instead of throwing.</summary>
    private static bool TryAddDays(DateTimeOffset value, long days, out DateTimeOffset result)
    {
        try
        {
            result = value.AddDays(days);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            result = default;
            return false;
        }
    }
}
