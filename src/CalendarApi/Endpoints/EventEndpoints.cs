using CalendarApi.Models;
using CalendarApi.Services;

namespace CalendarApi.Endpoints;

/// <summary>CRUD for events inside a calendar, plus expansion of recurring events into occurrences.</summary>
public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/calendars/{calendarId:guid}/events").WithTags("Events");

        // Create
        group.MapPost("/", (Guid calendarId, EventRequest request, ICalendarStore store, BookingService booking) =>
        {
            if (store.GetCalendar(calendarId) is null)
                return Results.NotFound();

            var errors = RequestValidator.Validate(request);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            // The booking service rejects unknown resources and double-bookings before saving.
            var result = booking.Save(new CalendarEvent(
                Guid.NewGuid(), calendarId, request.Title!.Trim(), request.Description,
                request.Start, request.End, request.Recurrence, request.ResourceId), isNew: true);
            return result.Status == BookingStatus.Ok
                ? Results.Created($"/api/calendars/{calendarId}/events/{result.Event!.Id}", result.Event)
                : BookingFailure(result);
        });

        // Read (list stored events; recurring events appear once, as their master record)
        group.MapGet("/", (Guid calendarId, ICalendarStore store) =>
            store.GetCalendar(calendarId) is null ? Results.NotFound() : Results.Ok(store.ListEvents(calendarId)));

        // Read (single)
        group.MapGet("/{eventId:guid}", (Guid calendarId, Guid eventId, ICalendarStore store) =>
            store.GetEvent(calendarId, eventId) is { } ev ? Results.Ok(ev) : Results.NotFound());

        // Update (full replacement; for a recurring event this changes the whole series)
        group.MapPut("/{eventId:guid}", (Guid calendarId, Guid eventId, EventRequest request, ICalendarStore store, BookingService booking) =>
        {
            if (store.GetEvent(calendarId, eventId) is not { } existing)
                return Results.NotFound();

            var errors = RequestValidator.Validate(request);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var updated = existing with
            {
                Title = request.Title!.Trim(),
                Description = request.Description,
                Start = request.Start,
                End = request.End,
                Recurrence = request.Recurrence,
                ResourceId = request.ResourceId
            };
            var result = booking.Save(updated, isNew: false);
            return result.Status == BookingStatus.Ok ? Results.Ok(result.Event) : BookingFailure(result);
        });

        // Delete (for a recurring event this removes the whole series)
        group.MapDelete("/{eventId:guid}", (Guid calendarId, Guid eventId, ICalendarStore store) =>
            store.DeleteEvent(calendarId, eventId) ? Results.NoContent() : Results.NotFound());

        // Occurrences: every concrete instance overlapping [from, to), with recurring events expanded.
        app.MapGet("/api/calendars/{calendarId:guid}/occurrences",
            (Guid calendarId, DateTimeOffset? from, DateTimeOffset? to, ICalendarStore store) =>
                store.GetCalendar(calendarId) is null
                    ? Results.NotFound()
                    : ExpandWindow(store.ListEvents(calendarId), from, to)).WithTags("Events");

        return app;
    }

    /// <summary>Validates a query window and expands events into occurrences (shared with resource bookings).</summary>
    internal static IResult ExpandWindow(IEnumerable<CalendarEvent> events, DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from is null || to is null)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["window"] = ["Both 'from' and 'to' query parameters are required."]
            });
        if (to <= from)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["to"] = ["'to' must be after 'from'."]
            });

        var occurrences = events
            .SelectMany(ev => RecurrenceExpander.Expand(ev, from.Value, to.Value))
            .OrderBy(o => o.Start)
            .Take(RecurrenceExpander.MaxOccurrences)
            .ToList();
        return Results.Ok(occurrences);
    }

    /// <summary>Maps a non-Ok booking outcome to its HTTP response.</summary>
    private static IResult BookingFailure(BookingResult result) => result.Status switch
    {
        BookingStatus.EventNotFound => Results.NotFound(),
        BookingStatus.ResourceNotFound => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["resourceId"] = ["Resource does not exist."]
        }),
        _ => Results.Conflict(new
        {
            message = "The resource is already booked for an overlapping time.",
            conflictingEventId = result.Conflict!.Id,
            conflictingCalendarId = result.Conflict.CalendarId
        })
    };
}
