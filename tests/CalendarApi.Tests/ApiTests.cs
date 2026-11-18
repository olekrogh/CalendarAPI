using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CalendarApi.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CalendarApi.Tests;

/// <summary>
/// End-to-end HTTP tests against the real pipeline. Each test class instance gets a fresh app,
/// so the singleton in-memory store starts empty every time.
/// </summary>
public class ApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly DateTimeOffset Start = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero); // a Monday

    private readonly TestApp _factory = new();
    private readonly HttpClient _client;

    public ApiTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task<Calendar> CreateCalendar(string owner = "alice", string name = "Work")
    {
        var response = await _client.PostAsJsonAsync("/api/calendars", new CreateCalendarRequest(owner, name, null), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Calendar>(Json))!;
    }

    private async Task<CalendarEvent> CreateEvent(Guid calendarId, RecurrenceRule? rule = null, string title = "Stand-up")
    {
        var response = await _client.PostAsJsonAsync($"/api/calendars/{calendarId}/events",
            new EventRequest(title, null, Start, Start.AddHours(1), rule), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CalendarEvent>(Json))!;
    }

    private async Task<List<EventOccurrence>> Occurrences(Guid calendarId, DateTimeOffset from, DateTimeOffset to)
    {
        var url = $"/api/calendars/{calendarId}/occurrences?from={Uri.EscapeDataString(from.ToString("o"))}&to={Uri.EscapeDataString(to.ToString("o"))}";
        return (await _client.GetFromJsonAsync<List<EventOccurrence>>(url, Json))!;
    }

    // ---- calendars -----------------------------------------------------------------------------

    [Fact]
    public async Task CreateCalendar_ReturnsCreatedWithLocation()
    {
        var response = await _client.PostAsJsonAsync("/api/calendars", new CreateCalendarRequest("alice", " Work ", "desc"), Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var calendar = (await response.Content.ReadFromJsonAsync<Calendar>(Json))!;
        Assert.Equal("Work", calendar.Name); // trimmed
        Assert.Equal($"/api/calendars/{calendar.Id}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task CreateCalendar_Invalid_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/calendars", new CreateCalendarRequest("", "", null), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetCalendar_ExistingAndMissing()
    {
        var calendar = await CreateCalendar();

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/calendars/{calendar.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/calendars/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task ListCalendars_SupportsMultipleCalendarsPerUser_AndOwnerFilter()
    {
        await CreateCalendar("alice", "Work");
        await CreateCalendar("alice", "Personal");
        await CreateCalendar("bob", "Bob's");

        var all = await _client.GetFromJsonAsync<List<Calendar>>("/api/calendars", Json);
        var alices = await _client.GetFromJsonAsync<List<Calendar>>("/api/calendars?ownerId=alice", Json);

        Assert.Equal(3, all!.Count);
        Assert.Equal(["Personal", "Work"], alices!.Select(c => c.Name));
    }

    [Fact]
    public async Task UpdateCalendar_ChangesNameButNotOwner()
    {
        var calendar = await CreateCalendar("alice", "Work");

        var response = await _client.PutAsJsonAsync($"/api/calendars/{calendar.Id}", new UpdateCalendarRequest("Renamed", "new"), Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<Calendar>(Json))!;
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal("alice", updated.OwnerId);
    }

    [Fact]
    public async Task UpdateCalendar_MissingReturns404_InvalidReturns400()
    {
        var calendar = await CreateCalendar();

        var missing = await _client.PutAsJsonAsync($"/api/calendars/{Guid.NewGuid()}", new UpdateCalendarRequest("x", null), Json);
        var invalid = await _client.PutAsJsonAsync($"/api/calendars/{calendar.Id}", new UpdateCalendarRequest("", null), Json);

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task DeleteCalendar_Returns204_ThenEventsAreGone()
    {
        var calendar = await CreateCalendar();
        await CreateEvent(calendar.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/calendars/{calendar.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/calendars/{calendar.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/calendars/{calendar.Id}/events")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/calendars/{calendar.Id}")).StatusCode);
    }

    // ---- events --------------------------------------------------------------------------------

    [Fact]
    public async Task CreateEvent_InMissingCalendar_Returns404()
    {
        var response = await _client.PostAsJsonAsync($"/api/calendars/{Guid.NewGuid()}/events",
            new EventRequest("x", null, Start, Start.AddHours(1), null), Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_Invalid_Returns400()
    {
        var calendar = await CreateCalendar();

        var response = await _client.PostAsJsonAsync($"/api/calendars/{calendar.Id}/events",
            new EventRequest("x", null, Start, Start.AddHours(-1), null), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAndListEvents_ReturnStoredEvents()
    {
        var calendar = await CreateCalendar();
        var ev = await CreateEvent(calendar.Id, title: "One");

        var single = await _client.GetFromJsonAsync<CalendarEvent>($"/api/calendars/{calendar.Id}/events/{ev.Id}", Json);
        var list = await _client.GetFromJsonAsync<List<CalendarEvent>>($"/api/calendars/{calendar.Id}/events", Json);

        Assert.Equal("One", single!.Title);
        Assert.Single(list!);
    }

    [Fact]
    public async Task GetEvent_ThroughWrongCalendar_Returns404()
    {
        var calendar = await CreateCalendar();
        var other = await CreateCalendar(name: "Other");
        var ev = await CreateEvent(calendar.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/calendars/{other.Id}/events/{ev.Id}")).StatusCode);
    }

    [Fact]
    public async Task UpdateEvent_ReplacesFieldsIncludingRecurrence()
    {
        var calendar = await CreateCalendar();
        var ev = await CreateEvent(calendar.Id);

        var response = await _client.PutAsJsonAsync($"/api/calendars/{calendar.Id}/events/{ev.Id}",
            new EventRequest("Renamed", "d", Start, Start.AddHours(2), new RecurrenceRule(RecurrenceFrequency.Daily, Count: 3)), Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<CalendarEvent>(Json))!;
        Assert.Equal("Renamed", updated.Title);
        Assert.Equal(RecurrenceFrequency.Daily, updated.Recurrence!.Frequency);
        Assert.Equal(3, (await Occurrences(calendar.Id, Start, Start.AddDays(30))).Count);
    }

    [Fact]
    public async Task UpdateEvent_MissingReturns404_InvalidReturns400()
    {
        var calendar = await CreateCalendar();
        var ev = await CreateEvent(calendar.Id);

        var missing = await _client.PutAsJsonAsync($"/api/calendars/{calendar.Id}/events/{Guid.NewGuid()}",
            new EventRequest("x", null, Start, Start.AddHours(1), null), Json);
        var invalid = await _client.PutAsJsonAsync($"/api/calendars/{calendar.Id}/events/{ev.Id}",
            new EventRequest("", null, Start, Start.AddHours(1), null), Json);

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_RemovesWholeSeries()
    {
        var calendar = await CreateCalendar();
        var ev = await CreateEvent(calendar.Id, new RecurrenceRule(RecurrenceFrequency.Daily));

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/calendars/{calendar.Id}/events/{ev.Id}")).StatusCode);
        Assert.Empty(await Occurrences(calendar.Id, Start, Start.AddDays(10)));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/calendars/{calendar.Id}/events/{ev.Id}")).StatusCode);
    }

    // ---- recurrence over HTTP --------------------------------------------------------------------

    [Fact]
    public async Task Occurrences_DailySeries_AreExpandedWithinWindow()
    {
        var calendar = await CreateCalendar();
        await CreateEvent(calendar.Id, new RecurrenceRule(RecurrenceFrequency.Daily));

        var result = await Occurrences(calendar.Id, Start, Start.AddDays(7));

        Assert.Equal(7, result.Count);
        Assert.All(result, o => Assert.True(o.IsRecurring));
    }

    [Fact]
    public async Task Occurrences_WeeklyMonWedFri_SerialisesEnumsAsStrings()
    {
        var calendar = await CreateCalendar();
        // Raw JSON proves the wire format uses names rather than numbers.
        var body = $$$"""
            {"title":"Gym","start":"{{{Start:o}}}","end":"{{{Start.AddHours(1):o}}}",
             "recurrence":{"frequency":"Weekly","interval":1,"daysOfWeek":["Monday","Wednesday","Friday"]}}
            """;
        var created = await _client.PostAsync($"/api/calendars/{calendar.Id}/events",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var result = await Occurrences(calendar.Id, Start, Start.AddDays(14));

        Assert.Equal(6, result.Count);
        Assert.Equal(DayOfWeek.Wednesday, result[1].Start.DayOfWeek);
    }

    [Fact]
    public async Task Occurrences_MergeOneOffAndRecurringEvents_InChronologicalOrder()
    {
        var calendar = await CreateCalendar();
        await CreateEvent(calendar.Id, new RecurrenceRule(RecurrenceFrequency.Daily, Count: 3), "Daily");
        await _client.PostAsJsonAsync($"/api/calendars/{calendar.Id}/events",
            new EventRequest("Lunch", null, Start.AddHours(3), Start.AddHours(4), null), Json);

        var result = await Occurrences(calendar.Id, Start, Start.AddDays(5));

        Assert.Equal(4, result.Count);
        Assert.Equal(result.OrderBy(o => o.Start).Select(o => o.Start), result.Select(o => o.Start));
        Assert.Equal("Lunch", result[1].Title);
    }

    [Fact]
    public async Task Occurrences_AreIsolatedPerCalendar()
    {
        var work = await CreateCalendar("alice", "Work");
        var home = await CreateCalendar("alice", "Home");
        await CreateEvent(work.Id, new RecurrenceRule(RecurrenceFrequency.Daily));

        Assert.Empty(await Occurrences(home.Id, Start, Start.AddDays(7)));
    }

    [Fact]
    public async Task Occurrences_MissingWindowOrBadWindow_Returns400()
    {
        var calendar = await CreateCalendar();

        var missing = await _client.GetAsync($"/api/calendars/{calendar.Id}/occurrences");
        var reversed = await _client.GetAsync(
            $"/api/calendars/{calendar.Id}/occurrences?from={Uri.EscapeDataString(Start.AddDays(1).ToString("o"))}&to={Uri.EscapeDataString(Start.ToString("o"))}");

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
    }

    [Fact]
    public async Task Occurrences_UnknownCalendar_Returns404()
    {
        var response = await _client.GetAsync(
            $"/api/calendars/{Guid.NewGuid()}/occurrences?from={Uri.EscapeDataString(Start.ToString("o"))}&to={Uri.EscapeDataString(Start.AddDays(1).ToString("o"))}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
