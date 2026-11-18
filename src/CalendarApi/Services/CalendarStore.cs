using System.Collections.Concurrent;
using CalendarApi.Models;

namespace CalendarApi.Services;

/// <summary>Persistence abstraction so the in-memory store can be swapped for a database later.</summary>
public interface ICalendarStore
{
    Calendar AddCalendar(Calendar calendar);
    Calendar? GetCalendar(Guid id);
    IReadOnlyList<Calendar> ListCalendars(string? ownerId);
    bool UpdateCalendar(Calendar calendar);

    /// <summary>Deletes the calendar and all of its events.</summary>
    bool DeleteCalendar(Guid id);

    CalendarEvent AddEvent(CalendarEvent ev);
    CalendarEvent? GetEvent(Guid calendarId, Guid eventId);
    IReadOnlyList<CalendarEvent> ListEvents(Guid calendarId);
    bool UpdateEvent(CalendarEvent ev);
    bool DeleteEvent(Guid calendarId, Guid eventId);

    /// <summary>All events, across every calendar, that book the given resource.</summary>
    IReadOnlyList<CalendarEvent> ListEventsForResource(Guid resourceId);

    Resource AddResource(Resource resource);
    Resource? GetResource(Guid id);
    IReadOnlyList<Resource> ListResources();
    bool UpdateResource(Resource resource);
    bool DeleteResource(Guid id);
}

/// <summary>Thread-safe, process-local store. Data is lost on restart.</summary>
public sealed class InMemoryCalendarStore : ICalendarStore
{
    private readonly ConcurrentDictionary<Guid, Calendar> _calendars = new();
    private readonly ConcurrentDictionary<Guid, CalendarEvent> _events = new();
    private readonly ConcurrentDictionary<Guid, Resource> _resources = new();

    public Calendar AddCalendar(Calendar calendar)
    {
        _calendars[calendar.Id] = calendar;
        return calendar;
    }

    public Calendar? GetCalendar(Guid id) => _calendars.GetValueOrDefault(id);

    public IReadOnlyList<Calendar> ListCalendars(string? ownerId) =>
        _calendars.Values
            .Where(c => ownerId is null || string.Equals(c.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public bool UpdateCalendar(Calendar calendar) =>
        _calendars.TryGetValue(calendar.Id, out var existing) && _calendars.TryUpdate(calendar.Id, calendar, existing);

    public bool DeleteCalendar(Guid id)
    {
        if (!_calendars.TryRemove(id, out _))
            return false;

        // Cascade: events cannot outlive their calendar.
        foreach (var ev in _events.Values.Where(e => e.CalendarId == id))
            _events.TryRemove(ev.Id, out _);
        return true;
    }

    public CalendarEvent AddEvent(CalendarEvent ev)
    {
        _events[ev.Id] = ev;
        return ev;
    }

    public CalendarEvent? GetEvent(Guid calendarId, Guid eventId) =>
        _events.TryGetValue(eventId, out var ev) && ev.CalendarId == calendarId ? ev : null;

    public IReadOnlyList<CalendarEvent> ListEvents(Guid calendarId) =>
        _events.Values.Where(e => e.CalendarId == calendarId).OrderBy(e => e.Start).ToList();

    public bool UpdateEvent(CalendarEvent ev) =>
        GetEvent(ev.CalendarId, ev.Id) is { } existing && _events.TryUpdate(ev.Id, ev, existing);

    public bool DeleteEvent(Guid calendarId, Guid eventId) =>
        GetEvent(calendarId, eventId) is not null && _events.TryRemove(eventId, out _);

    public IReadOnlyList<CalendarEvent> ListEventsForResource(Guid resourceId) =>
        _events.Values.Where(e => e.ResourceId == resourceId).OrderBy(e => e.Start).ToList();

    public Resource AddResource(Resource resource)
    {
        _resources[resource.Id] = resource;
        return resource;
    }

    public Resource? GetResource(Guid id) => _resources.GetValueOrDefault(id);

    public IReadOnlyList<Resource> ListResources() =>
        _resources.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public bool UpdateResource(Resource resource) =>
        _resources.TryGetValue(resource.Id, out var existing) && _resources.TryUpdate(resource.Id, resource, existing);

    public bool DeleteResource(Guid id) => _resources.TryRemove(id, out _);
}
