namespace CalendarApi.Models;

/// <summary>Supported repetition frequencies.</summary>
public enum RecurrenceFrequency
{
    Daily,
    Weekly
}

/// <summary>
/// Describes how an event repeats.
/// </summary>
/// <param name="Frequency">Daily or Weekly.</param>
/// <param name="Interval">Repeat every N days/weeks (1 = every day/week).</param>
/// <param name="DaysOfWeek">
/// Weekly only: the weekdays on which the event occurs. When omitted, the weekday of the event's start is used.
/// </param>
/// <param name="Until">Optional inclusive end of the series. Mutually exclusive with <paramref name="Count"/>.</param>
/// <param name="Count">Optional total number of occurrences. Mutually exclusive with <paramref name="Until"/>.</param>
public sealed record RecurrenceRule(
    RecurrenceFrequency Frequency,
    int Interval = 1,
    IReadOnlyList<DayOfWeek>? DaysOfWeek = null,
    DateTimeOffset? Until = null,
    int? Count = null);

/// <summary>A calendar belonging to a user. A user may own many calendars.</summary>
public sealed record Calendar(Guid Id, string OwnerId, string Name, string? Description);

/// <summary>
/// A stored event. When <see cref="Recurrence"/> is set this is the "master" of a series;
/// individual occurrences are computed on demand and never stored.
/// </summary>
public sealed record CalendarEvent(
    Guid Id,
    Guid CalendarId,
    string Title,
    string? Description,
    DateTimeOffset Start,
    DateTimeOffset End,
    RecurrenceRule? Recurrence,
    Guid? ResourceId = null);

/// <summary>A single concrete instance of an event (one-off or part of a series).</summary>
public sealed record EventOccurrence(
    Guid EventId,
    Guid CalendarId,
    string Title,
    string? Description,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsRecurring,
    Guid? ResourceId = null);

/// <summary>Request body for creating a calendar.</summary>
public sealed record CreateCalendarRequest(string? OwnerId, string? Name, string? Description);

/// <summary>Request body for updating a calendar. The owner cannot be changed.</summary>
public sealed record UpdateCalendarRequest(string? Name, string? Description);

/// <summary>Request body for creating or replacing an event.</summary>
public sealed record EventRequest(
    string? Title,
    string? Description,
    DateTimeOffset Start,
    DateTimeOffset End,
    RecurrenceRule? Recurrence,
    Guid? ResourceId = null);

/// <summary>A bookable resource such as a meeting room. Shared across all calendars and users.</summary>
public sealed record Resource(Guid Id, string Name, string? Description, int? Capacity);

/// <summary>Request body for creating or replacing a resource.</summary>
public sealed record ResourceRequest(string? Name, string? Description, int? Capacity);
