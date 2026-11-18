using CalendarApi.Models;
using CalendarApi.Services;

namespace CalendarApi.Endpoints;

/// <summary>CRUD for bookable resources (meeting rooms) and a view of their bookings.</summary>
public static class ResourceEndpoints
{
    public static IEndpointRouteBuilder MapResourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/resources").WithTags("Resources");

        // Create
        group.MapPost("/", (ResourceRequest request, ICalendarStore store) =>
        {
            var errors = RequestValidator.Validate(request);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var resource = store.AddResource(new Resource(
                Guid.NewGuid(), request.Name!.Trim(), request.Description, request.Capacity));
            return Results.Created($"/api/resources/{resource.Id}", resource);
        });

        // Read
        group.MapGet("/", (ICalendarStore store) => Results.Ok(store.ListResources()));

        group.MapGet("/{id:guid}", (Guid id, ICalendarStore store) =>
            store.GetResource(id) is { } resource ? Results.Ok(resource) : Results.NotFound());

        // Update
        group.MapPut("/{id:guid}", (Guid id, ResourceRequest request, ICalendarStore store) =>
        {
            if (store.GetResource(id) is not { } existing)
                return Results.NotFound();

            var errors = RequestValidator.Validate(request);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var updated = existing with
            {
                Name = request.Name!.Trim(),
                Description = request.Description,
                Capacity = request.Capacity
            };
            return store.UpdateResource(updated) ? Results.Ok(updated) : Results.NotFound();
        });

        // Delete (refused while events still book the resource, so no event is left pointing at nothing)
        group.MapDelete("/{id:guid}", (Guid id, ICalendarStore store) =>
        {
            if (store.GetResource(id) is null)
                return Results.NotFound();
            if (store.ListEventsForResource(id).Count > 0)
                return Results.Conflict(new { message = "The resource still has events booked; remove or move them first." });

            return store.DeleteResource(id) ? Results.NoContent() : Results.NotFound();
        });

        // Bookings: occurrences from every calendar that use this resource, within [from, to).
        group.MapGet("/{id:guid}/occurrences", (Guid id, DateTimeOffset? from, DateTimeOffset? to, ICalendarStore store) =>
            store.GetResource(id) is null
                ? Results.NotFound()
                : EventEndpoints.ExpandWindow(store.ListEventsForResource(id), from, to));

        return app;
    }
}
