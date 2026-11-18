using CalendarApi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CalendarApi.Tests;

/// <summary>
/// The real app hosted in-process, pointed at its own throw-away SQLite file so every test starts with an empty database.
/// </summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    private readonly string _dbPath = TempDb.NewPath();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:CalendarDb", $"Data Source={_dbPath}");

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            TempDb.Delete(_dbPath);
    }
}

/// <summary>A throw-away SQLite database with a ready-to-use <see cref="EfCalendarStore"/>.</summary>
public sealed class SqliteTestDb : IDisposable
{
    private sealed class Factory(DbContextOptions<CalendarDbContext> options) : IDbContextFactory<CalendarDbContext>
    {
        public CalendarDbContext CreateDbContext() => new(options);
    }

    public string Path { get; } = TempDb.NewPath();

    public SqliteTestDb()
    {
        using var db = NewFactory().CreateDbContext();
        db.Database.EnsureCreated();
    }

    /// <summary>A fresh store over the same file, to prove data survives beyond one store instance.</summary>
    public EfCalendarStore NewStore() => new(NewFactory());

    private Factory NewFactory() =>
        new(new DbContextOptionsBuilder<CalendarDbContext>().UseSqlite($"Data Source={Path}").Options);

    public void Dispose() => TempDb.Delete(Path);
}

internal static class TempDb
{
    public static string NewPath() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"calendar-tests-{Guid.NewGuid():N}.db");

    public static void Delete(string path)
    {
        SqliteConnection.ClearAllPools(); // release the file handle so Windows lets us delete it
        try { File.Delete(path); } catch (IOException) { /* best effort: it lives in the temp folder */ }
    }
}
