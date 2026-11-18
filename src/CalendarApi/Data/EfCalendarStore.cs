using CalendarApi.Models;
using CalendarApi.Services;
using Microsoft.EntityFrameworkCore;

namespace CalendarApi.Data;

/// <summary>
/// SQLite-backed store. Registered as a singleton, so it creates a short-lived DbContext per call.
/// </summary>
/// <remarks>
/// SQLite cannot order or compare <see cref="DateTimeOffset"/> values in SQL, so those sorts happen in memory.
/// Updates use "update by key" and treat a zero-row result as "not found".
/// </remarks>
public sealed class EfCalendarStore(IDbContextFactory<CalendarDbContext> factory) : ICalendarStore
{
    // ---- calendars -----------------------------------------------------------------------------

    public Calendar AddCalendar(Calendar calendar)
    {
        using var db = factory.CreateDbContext();
        db.Calendars.Add(calendar);
        db.SaveChanges();
        return calendar;
    }

    public Calendar? GetCalendar(Guid id)
    {
        using var db = factory.CreateDbContext();
        return db.Calendars.AsNoTracking().FirstOrDefault(c => c.Id == id);
    }

    public IReadOnlyList<Calendar> ListCalendars(string? ownerId)
    {
        using var db = factory.CreateDbContext();
        var query = db.Calendars.AsNoTracking();
        if (ownerId is not null)
        {
            var owner = ownerId.ToLower();
            query = query.Where(c => c.OwnerId.ToLower() == owner); // case-insensitive owner match
        }

        return query.ToList().OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool UpdateCalendar(Calendar calendar)
    {
        using var db = factory.CreateDbContext();
        try
        {
            db.Calendars.Update(calendar);
            return db.SaveChanges() > 0;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false; // row no longer exists
        }
    }

    public bool DeleteCalendar(Guid id)
    {
        using var db = factory.CreateDbContext();
        // The foreign key cascades to the calendar's events.
        return db.Calendars.Where(c => c.Id == id).ExecuteDelete() > 0;
    }

    // ---- events --------------------------------------------------------------------------------

    public CalendarEvent AddEvent(CalendarEvent ev)
    {
        using var db = factory.CreateDbContext();
        db.Events.Add(ev);
        db.SaveChanges();
        return ev;
    }

    public CalendarEvent? GetEvent(Guid calendarId, Guid eventId)
    {
        using var db = factory.CreateDbContext();
        return db.Events.AsNoTracking().FirstOrDefault(e => e.Id == eventId && e.CalendarId == calendarId);
    }

    public IReadOnlyList<CalendarEvent> ListEvents(Guid calendarId)
    {
        using var db = factory.CreateDbContext();
        return db.Events.AsNoTracking().Where(e => e.CalendarId == calendarId).ToList().OrderBy(e => e.Start).ToList();
    }

    public bool UpdateEvent(CalendarEvent ev)
    {
        using var db = factory.CreateDbContext();

        // An event may only be updated through the calendar it belongs to.
        if (!db.Events.Any(e => e.Id == ev.Id && e.CalendarId == ev.CalendarId))
            return false;

        try
        {
            db.Events.Update(ev);
            return db.SaveChanges() > 0;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public bool DeleteEvent(Guid calendarId, Guid eventId)
    {
        using var db = factory.CreateDbContext();
        return db.Events.Where(e => e.Id == eventId && e.CalendarId == calendarId).ExecuteDelete() > 0;
    }

    public IReadOnlyList<CalendarEvent> ListEventsForResource(Guid resourceId)
    {
        using var db = factory.CreateDbContext();
        return db.Events.AsNoTracking().Where(e => e.ResourceId == resourceId).ToList().OrderBy(e => e.Start).ToList();
    }

    // ---- resources -----------------------------------------------------------------------------

    public Resource AddResource(Resource resource)
    {
        using var db = factory.CreateDbContext();
        db.Resources.Add(resource);
        db.SaveChanges();
        return resource;
    }

    public Resource? GetResource(Guid id)
    {
        using var db = factory.CreateDbContext();
        return db.Resources.AsNoTracking().FirstOrDefault(r => r.Id == id);
    }

    public IReadOnlyList<Resource> ListResources()
    {
        using var db = factory.CreateDbContext();
        return db.Resources.AsNoTracking().ToList().OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool UpdateResource(Resource resource)
    {
        using var db = factory.CreateDbContext();
        try
        {
            db.Resources.Update(resource);
            return db.SaveChanges() > 0;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    /// <summary>Throws if events still reference the resource; callers check <see cref="ListEventsForResource"/> first.</summary>
    public bool DeleteResource(Guid id)
    {
        using var db = factory.CreateDbContext();
        return db.Resources.Where(r => r.Id == id).ExecuteDelete() > 0;
    }
}
