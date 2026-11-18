using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CalendarApi.Models;
using CalendarApi.Services;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CalendarApi.Tests;

/// <summary>Unit tests for double-booking protection.</summary>
public class BookingServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero); // a Monday

    private readonly InMemoryCalendarStore _store = new();
    private readonly BookingService _booking;
    private readonly Resource _room;
    private readonly Calendar _calendar;

    public BookingServiceTests()
    {
        _booking = new BookingService(_store);
        _room = _store.AddResource(new Resource(Guid.NewGuid(), "Room 1", null, 8));
        _calendar = _store.AddCalendar(new Calendar(Guid.NewGuid(), "alice", "Work", null));
    }

    private CalendarEvent Event(DateTimeOffset start, TimeSpan? length = null, RecurrenceRule? rule = null, Guid? resource = null) =>
        new(Guid.NewGuid(), _calendar.Id, "Meeting", null, start, start + (length ?? TimeSpan.FromHours(1)), rule,
            resource ?? _room.Id);

    [Fact]
    public void Save_WithUnknownResource_IsRejected() =>
        Assert.Equal(BookingStatus.ResourceNotFound, _booking.Save(Event(Start, resource: Guid.NewGuid()), true).Status);

    [Fact]
    public void Save_WithoutResource_NeverConflicts()
    {
        var a = new CalendarEvent(Guid.NewGuid(), _calendar.Id, "A", null, Start, Start.AddHours(1), null);
        var b = a with { Id = Guid.NewGuid() };

        Assert.Equal(BookingStatus.Ok, _booking.Save(a, true).Status);
        Assert.Equal(BookingStatus.Ok, _booking.Save(b, true).Status);
    }

    [Fact]
    public void Save_OverlappingBooking_ReportsConflictingEvent()
    {
        var first = Event(Start);
        _booking.Save(first, true);

        var result = _booking.Save(Event(Start.AddMinutes(30)), true);

        Assert.Equal(BookingStatus.Conflict, result.Status);
        Assert.Equal(first.Id, result.Conflict!.Id);
    }

    [Fact]
    public void Save_BackToBackBookings_AreAllowed()
    {
        _booking.Save(Event(Start), true);

        Assert.Equal(BookingStatus.Ok, _booking.Save(Event(Start.AddHours(1)), true).Status);
    }

    [Fact]
    public void Save_SameTimeDifferentRoom_IsAllowed()
    {
        var other = _store.AddResource(new Resource(Guid.NewGuid(), "Room 2", null, null));
        _booking.Save(Event(Start), true);

        Assert.Equal(BookingStatus.Ok, _booking.Save(Event(Start, resource: other.Id), true).Status);
    }

    [Fact]
    public void Save_ConflictsAcrossCalendars()
    {
        var bobsCalendar = _store.AddCalendar(new Calendar(Guid.NewGuid(), "bob", "Bob", null));
        _booking.Save(Event(Start), true);

        var clash = Event(Start) with { CalendarId = bobsCalendar.Id };

        Assert.Equal(BookingStatus.Conflict, _booking.Save(clash, true).Status);
    }

    [Fact]
    public void Save_OneOffInsideRecurringSeries_Conflicts()
    {
        _booking.Save(Event(Start, rule: new RecurrenceRule(RecurrenceFrequency.Daily)), true);

        // Three weeks later at the series' time of day.
        Assert.Equal(BookingStatus.Conflict, _booking.Save(Event(Start.AddDays(21)), true).Status);
        // Same day but outside the series' hour is fine.
        Assert.Equal(BookingStatus.Ok, _booking.Save(Event(Start.AddDays(21).AddHours(3)), true).Status);
    }

    [Fact]
    public void Save_NewSeriesHittingExistingOneOff_Conflicts()
    {
        _booking.Save(Event(Start.AddDays(14)), true);

        var series = Event(Start, rule: new RecurrenceRule(RecurrenceFrequency.Weekly, Count: 5));

        Assert.Equal(BookingStatus.Conflict, _booking.Save(series, true).Status);
    }

    [Fact]
    public void Save_InterleavedWeeklySeries_DoNotConflict()
    {
        _booking.Save(Event(Start, rule: new RecurrenceRule(RecurrenceFrequency.Weekly, Interval: 2)), true);

        // Same weekday, but offset by one week: never lands on the same day.
        var other = Event(Start.AddDays(7), rule: new RecurrenceRule(RecurrenceFrequency.Weekly, Interval: 2));

        Assert.Equal(BookingStatus.Ok, _booking.Save(other, true).Status);
    }

    [Fact]
    public void Save_SeriesEndedBeforeNewBooking_DoesNotConflict()
    {
        _booking.Save(Event(Start, rule: new RecurrenceRule(RecurrenceFrequency.Daily, Count: 3)), true);

        Assert.Equal(BookingStatus.Ok, _booking.Save(Event(Start.AddDays(10)), true).Status);
    }

    [Fact]
    public void Update_DoesNotConflictWithItself()
    {
        var ev = Event(Start);
        _booking.Save(ev, true);

        var result = _booking.Save(ev with { End = ev.End.AddMinutes(30) }, isNew: false);

        Assert.Equal(BookingStatus.Ok, result.Status);
    }

    [Fact]
    public void Update_UnknownEvent_ReturnsEventNotFound() =>
        Assert.Equal(BookingStatus.EventNotFound, _booking.Save(Event(Start), isNew: false).Status);
}

/// <summary>The OpenAPI document and Swagger UI are served in Development (the factory's default environment).</summary>
public class OpenApiTests : IClassFixture<TestApp>
{
    private readonly HttpClient _client;

    public OpenApiTests(TestApp factory) => _client = factory.CreateClient();

    [Fact]
    public async Task OpenApiDocument_DescribesAllEndpointGroups()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/calendars", json);
        Assert.Contains("/api/resources", json);
        Assert.Contains("/occurrences", json);
    }

    [Fact]
    public async Task SwaggerUi_IsServed()
    {
        var response = await _client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

/// <summary>End-to-end HTTP tests for resources and room booking.</summary>
public class ResourceApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly DateTimeOffset Start = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly TestApp _factory = new();
    private readonly HttpClient _client;

    public ResourceApiTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<Resource> CreateResource(string name = "Room 1")
    {
        var response = await _client.PostAsJsonAsync("/api/resources", new ResourceRequest(name, null, 6), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Resource>(Json))!;
    }

    private async Task<Calendar> CreateCalendar(string owner = "alice")
    {
        var response = await _client.PostAsJsonAsync("/api/calendars", new CreateCalendarRequest(owner, "Work", null), Json);
        return (await response.Content.ReadFromJsonAsync<Calendar>(Json))!;
    }

    private Task<HttpResponseMessage> Book(Guid calendarId, Guid? resourceId, DateTimeOffset? start = null, RecurrenceRule? rule = null)
    {
        var s = start ?? Start;
        return _client.PostAsJsonAsync($"/api/calendars/{calendarId}/events",
            new EventRequest("Meeting", null, s, s.AddHours(1), rule, resourceId), Json);
    }

    [Fact]
    public async Task ResourceCrud_FullLifecycle()
    {
        var room = await CreateResource("Boardroom");

        var fetched = await _client.GetFromJsonAsync<Resource>($"/api/resources/{room.Id}", Json);
        Assert.Equal("Boardroom", fetched!.Name);
        Assert.Single((await _client.GetFromJsonAsync<List<Resource>>("/api/resources", Json))!);

        var put = await _client.PutAsJsonAsync($"/api/resources/{room.Id}", new ResourceRequest("Renamed", "d", 10), Json);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(10, (await put.Content.ReadFromJsonAsync<Resource>(Json))!.Capacity);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/resources/{room.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/resources/{room.Id}")).StatusCode);
    }

    [Fact]
    public async Task CreateResource_Invalid_Returns400()
    {
        var noName = await _client.PostAsJsonAsync("/api/resources", new ResourceRequest("", null, null), Json);
        var badCapacity = await _client.PostAsJsonAsync("/api/resources", new ResourceRequest("R", null, 0), Json);

        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badCapacity.StatusCode);
    }

    [Fact]
    public async Task UpdateResource_MissingReturns404_InvalidReturns400()
    {
        var room = await CreateResource();

        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PutAsJsonAsync($"/api/resources/{Guid.NewGuid()}", new ResourceRequest("x", null, null), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PutAsJsonAsync($"/api/resources/{room.Id}", new ResourceRequest("", null, null), Json)).StatusCode);
    }

    [Fact]
    public async Task DeleteResource_Missing_Returns404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/resources/{Guid.NewGuid()}")).StatusCode);

    [Fact]
    public async Task DeleteResource_WhileBooked_Returns409_UntilEventRemoved()
    {
        var room = await CreateResource();
        var calendar = await CreateCalendar();
        var ev = (await (await Book(calendar.Id, room.Id)).Content.ReadFromJsonAsync<CalendarEvent>(Json))!;

        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync($"/api/resources/{room.Id}")).StatusCode);

        await _client.DeleteAsync($"/api/calendars/{calendar.Id}/events/{ev.Id}");
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/resources/{room.Id}")).StatusCode);
    }

    [Fact]
    public async Task CreateEvent_WithResource_StoresResourceId()
    {
        var room = await CreateResource();
        var calendar = await CreateCalendar();

        var response = await Book(calendar.Id, room.Id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(room.Id, (await response.Content.ReadFromJsonAsync<CalendarEvent>(Json))!.ResourceId);
    }

    [Fact]
    public async Task CreateEvent_WithUnknownResource_Returns400()
    {
        var calendar = await CreateCalendar();

        Assert.Equal(HttpStatusCode.BadRequest, (await Book(calendar.Id, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task CreateEvent_DoubleBookingRoom_Returns409WithConflictDetails()
    {
        var room = await CreateResource();
        var alice = await CreateCalendar("alice");
        var bob = await CreateCalendar("bob");
        var first = (await (await Book(alice.Id, room.Id)).Content.ReadFromJsonAsync<CalendarEvent>(Json))!;

        var response = await Book(bob.Id, room.Id, Start.AddMinutes(15));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(first.Id.ToString(), body);
    }

    [Fact]
    public async Task CreateEvent_RecurringBookingClashingLater_Returns409()
    {
        var room = await CreateResource();
        var calendar = await CreateCalendar();
        await Book(calendar.Id, room.Id, Start.AddDays(14)); // one-off two weeks in

        var series = await Book(calendar.Id, room.Id, Start, new RecurrenceRule(RecurrenceFrequency.Weekly));

        Assert.Equal(HttpStatusCode.Conflict, series.StatusCode);
    }

    [Fact]
    public async Task UpdateEvent_MovingIntoBookedSlot_Returns409_ButOwnSlotIsFine()
    {
        var room = await CreateResource();
        var calendar = await CreateCalendar();
        await Book(calendar.Id, room.Id, Start);
        var second = (await (await Book(calendar.Id, room.Id, Start.AddHours(2))).Content.ReadFromJsonAsync<CalendarEvent>(Json))!;
        var url = $"/api/calendars/{calendar.Id}/events/{second.Id}";

        var clash = await _client.PutAsJsonAsync(url, new EventRequest("Meeting", null, Start, Start.AddHours(1), null, room.Id), Json);
        var same = await _client.PutAsJsonAsync(url, new EventRequest("Longer", null, second.Start, second.End.AddMinutes(15), null, room.Id), Json);

        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
    }

    [Fact]
    public async Task UpdateEvent_RemovingResource_FreesTheRoom()
    {
        var room = await CreateResource();
        var calendar = await CreateCalendar();
        var first = (await (await Book(calendar.Id, room.Id)).Content.ReadFromJsonAsync<CalendarEvent>(Json))!;

        await _client.PutAsJsonAsync($"/api/calendars/{calendar.Id}/events/{first.Id}",
            new EventRequest("Meeting", null, Start, Start.AddHours(1), null, null), Json);

        Assert.Equal(HttpStatusCode.Created, (await Book(calendar.Id, room.Id)).StatusCode);
    }

    [Fact]
    public async Task ResourceOccurrences_ListBookingsFromAllCalendars_AndTrackResourceId()
    {
        var room = await CreateResource();
        var alice = await CreateCalendar("alice");
        var bob = await CreateCalendar("bob");
        await Book(alice.Id, room.Id, Start, new RecurrenceRule(RecurrenceFrequency.Daily, Count: 2));
        await Book(bob.Id, room.Id, Start.AddHours(3));
        await Book(bob.Id, null, Start.AddHours(5)); // no room: must not appear

        var url = $"/api/resources/{room.Id}/occurrences?from={Uri.EscapeDataString(Start.ToString("o"))}&to={Uri.EscapeDataString(Start.AddDays(3).ToString("o"))}";
        var result = (await _client.GetFromJsonAsync<List<EventOccurrence>>(url, Json))!;

        Assert.Equal(3, result.Count);
        Assert.All(result, o => Assert.Equal(room.Id, o.ResourceId));
    }

    [Fact]
    public async Task ResourceOccurrences_UnknownResourceOr_MissingWindow()
    {
        var room = await CreateResource();

        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync($"/api/resources/{Guid.NewGuid()}/occurrences?from={Uri.EscapeDataString(Start.ToString("o"))}&to={Uri.EscapeDataString(Start.AddDays(1).ToString("o"))}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/resources/{room.Id}/occurrences")).StatusCode);
    }
}
