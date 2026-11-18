using CalendarApi.Models;

namespace CalendarApi.Services;

public enum BookingStatus
{
    Ok,
    EventNotFound,
    ResourceNotFound,
    Conflict
}

/// <param name="Status">Outcome of the save.</param>
/// <param name="Event">The saved event when <see cref="BookingStatus.Ok"/>.</param>
/// <param name="Conflict">The already-booked event that clashes, when <see cref="BookingStatus.Conflict"/>.</param>
public sealed record BookingResult(BookingStatus Status, CalendarEvent? Event = null, CalendarEvent? Conflict = null);

/// <summary>
/// Saves events and prevents a resource (e.g. a meeting room) from being double-booked,
/// including clashes between recurring series.
/// </summary>
public sealed class BookingService(ICalendarStore store)
{
    /// <summary>
    /// Open-ended series are only compared over this horizon from the candidate's start,
    /// because two infinite series could otherwise never be fully compared.
    /// </summary>
    public static readonly TimeSpan ConflictHorizon = TimeSpan.FromDays(730);

    // Check-then-write must be atomic, otherwise two concurrent requests could both book the same slot.
    private readonly object _gate = new();

    /// <summary>Adds (<paramref name="isNew"/>) or replaces an event after validating its resource booking.</summary>
    public BookingResult Save(CalendarEvent candidate, bool isNew)
    {
        lock (_gate)
        {
            if (candidate.ResourceId is { } resourceId)
            {
                if (store.GetResource(resourceId) is null)
                    return new BookingResult(BookingStatus.ResourceNotFound);

                if (FindConflict(candidate) is { } clash)
                    return new BookingResult(BookingStatus.Conflict, Conflict: clash);
            }

            if (isNew)
                store.AddEvent(candidate);
            else if (!store.UpdateEvent(candidate))
                return new BookingResult(BookingStatus.EventNotFound);

            return new BookingResult(BookingStatus.Ok, candidate);
        }
    }

    /// <summary>Returns the first existing booking of the same resource that overlaps the candidate, if any.</summary>
    private CalendarEvent? FindConflict(CalendarEvent candidate)
    {
        var from = candidate.Start;
        var to = candidate.Start + ConflictHorizon;
        if (candidate.End > to)
            to = candidate.End;

        var mine = RecurrenceExpander.Expand(candidate, from, to).ToList();

        foreach (var other in store.ListEventsForResource(candidate.ResourceId!.Value))
        {
            if (other.Id == candidate.Id)
                continue; // an event never conflicts with its previous version

            foreach (var theirs in RecurrenceExpander.Expand(other, from, to))
                if (mine.Any(m => m.Start < theirs.End && theirs.Start < m.End))
                    return other;
        }

        return null;
    }
}
