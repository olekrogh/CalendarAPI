using System.Text.Json;
using System.Text.Json.Serialization;
using CalendarApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CalendarApi.Data;

/// <summary>EF Core model for the SQLite database. The domain records are mapped directly as entities.</summary>
public sealed class CalendarDbContext(DbContextOptions<CalendarDbContext> options) : DbContext(options)
{
    // The recurrence rule is stored as one JSON text column on the event (enums as names, e.g. "Weekly").
    private static readonly JsonSerializerOptions RuleJson = new() { Converters = { new JsonStringEnumConverter() } };

    public DbSet<Calendar> Calendars => Set<Calendar>();
    public DbSet<CalendarEvent> Events => Set<CalendarEvent>();
    public DbSet<Resource> Resources => Set<Resource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Calendar>(e =>
        {
            e.Property(c => c.OwnerId).IsRequired().HasMaxLength(200);
            e.Property(c => c.Name).IsRequired().HasMaxLength(100);
            e.HasIndex(c => c.OwnerId); // "list a user's calendars"
        });

        modelBuilder.Entity<Resource>(e =>
        {
            e.Property(r => r.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<CalendarEvent>(e =>
        {
            e.Property(x => x.Title).IsRequired().HasMaxLength(200);
            e.Property(x => x.Recurrence).HasConversion(
                rule => JsonSerializer.Serialize(rule, RuleJson),
                json => JsonSerializer.Deserialize<RecurrenceRule>(json, RuleJson)!);

            // Deleting a calendar deletes its events.
            e.HasOne<Calendar>().WithMany().HasForeignKey(x => x.CalendarId).OnDelete(DeleteBehavior.Cascade);

            // A resource that still has events cannot be deleted (the database enforces this too).
            e.HasOne<Resource>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.CalendarId);
            e.HasIndex(x => x.ResourceId);
        });
    }
}
