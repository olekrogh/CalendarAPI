using System.Text.Json.Serialization;
using CalendarApi.Endpoints;
using CalendarApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Serialise enums (Frequency, DaysOfWeek) as readable strings, e.g. "Weekly", "Monday".
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Singleton because the store keeps all data in memory.
builder.Services.AddSingleton<ICalendarStore, InMemoryCalendarStore>();

// Singleton so its lock serialises every booking in the process.
builder.Services.AddSingleton<BookingService>();

// Built-in OpenAPI document generation; Swagger UI (below) renders it.
builder.Services.AddOpenApi();

var app = builder.Build();

// API docs are only exposed in Development: JSON at /openapi/v1.json, UI at /swagger.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "Calendar API v1");
        o.RoutePrefix = "swagger";
    });
}

app.MapCalendarEndpoints();
app.MapEventEndpoints();
app.MapResourceEndpoints();

app.Run();

/// <summary>Exposed so the integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
