using CalendarApi.Models;
using CalendarApi.Services;

namespace CalendarApi.Endpoints;

/// <summary>CRUD for calendars. Each calendar belongs to one owner (user); a user can have many.</summary>
public static class CalendarEndpoints
{
    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/calendars").WithTags("Calendars");

        // Create
        group.MapPost("/", (CreateCalendarRequest request, ICalendarStore store) =>
        {
            var errors = RequestValidator.Validate(request);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var calendar = store.AddCalendar(new Calendar(
                Guid.NewGuid(), request.OwnerId!.Trim(), request.Name!.Trim(), request.Description));
            return Results.Created($"/api/calendars/{calendar.Id}", calendar);
        });

        // Read (list, optionally filtered to one user's calendars)
        group.MapGet("/", (string? ownerId, ICalendarStore store) => Results.Ok(store.ListCalendars(ownerId)));

        // Read (single)
        group.MapGet("/{id:guid}", (Guid id, ICalendarStore store) =>
            store.GetCalendar(id) is { } calendar ? Results.Ok(calendar) : Results.NotFound());

        // Update
        group.MapPut("/{id:guid}", (Guid id, UpdateCalendarRequest request, ICalendarStore store) =>
        {
            if (store.GetCalendar(id) is not { } existing)
                return Results.NotFound();

            var errors = RequestValidator.Validate(request);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var updated = existing with { Name = request.Name!.Trim(), Description = request.Description };
            return store.UpdateCalendar(updated) ? Results.Ok(updated) : Results.NotFound();
        });

        // Delete (cascades to events)
        group.MapDelete("/{id:guid}", (Guid id, ICalendarStore store) =>
            store.DeleteCalendar(id) ? Results.NoContent() : Results.NotFound());

        return app;
    }
}
