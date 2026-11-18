using System.Text.Json.Serialization;
using CalendarApi.Data;
using CalendarApi.Endpoints;
using CalendarApi.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Serialise enums (Frequency, DaysOfWeek) as readable strings, e.g. "Weekly", "Monday".
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// SQLite via EF Core. The connection string is resolved lazily so tests can override it.
builder.Services.AddDbContextFactory<CalendarDbContext>((sp, options) =>
    options.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("CalendarDb")
                      ?? "Data Source=calendar.db"));

// Singleton is fine: the store opens a short-lived DbContext per call.
builder.Services.AddSingleton<ICalendarStore, EfCalendarStore>();

// Singleton so its lock serialises every booking in the process.
builder.Services.AddSingleton<BookingService>();

// Built-in OpenAPI document generation; Swagger UI (below) renders it.
builder.Services.AddOpenApi();

var app = builder.Build();

// Create the database and schema on first start (no migrations yet).
using (var db = app.Services.GetRequiredService<IDbContextFactory<CalendarDbContext>>().CreateDbContext())
    db.Database.EnsureCreated();

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
