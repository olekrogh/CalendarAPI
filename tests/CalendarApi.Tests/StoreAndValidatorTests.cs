using CalendarApi.Models;
using CalendarApi.Services;

namespace CalendarApi.Tests;

/// <summary>
/// Behaviour every <see cref="ICalendarStore"/> must have. Run against each implementation by the subclasses below,
/// so the in-memory and SQLite stores are proven interchangeable.
/// </summary>
public abstract class StoreContractTests(ICalendarStore store)
{
    private readonly ICalendarStore _store = store;

    private Calendar NewCalendar(string owner = "alice", string name = "Work") =>
        _store.AddCalendar(new Calendar(Guid.NewGuid(), owner, name, null));

    private CalendarEvent NewEvent(Guid calendarId) =>
        _store.AddEvent(new CalendarEvent(Guid.NewGuid(), calendarId, "Meeting", null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), null));

    [Fact]
    public void ListCalendars_FiltersByOwner_CaseInsensitively()
    {
        NewCalendar("alice", "A1");
        NewCalendar("alice", "A2");
        NewCalendar("bob", "B1");

        Assert.Equal(2, _store.ListCalendars("ALICE").Count);
        Assert.Single(_store.ListCalendars("bob"));
        Assert.Equal(3, _store.ListCalendars(null).Count);
    }

    [Fact]
    public void UpdateCalendar_UnknownId_ReturnsFalse() =>
        Assert.False(_store.UpdateCalendar(new Calendar(Guid.NewGuid(), "x", "y", null)));

    [Fact]
    public void UpdateCalendar_ReplacesStoredValue()
    {
        var calendar = NewCalendar();

        Assert.True(_store.UpdateCalendar(calendar with { Name = "Renamed" }));
        Assert.Equal("Renamed", _store.GetCalendar(calendar.Id)!.Name);
    }

    [Fact]
    public void DeleteCalendar_RemovesItsEvents_ButNotOthers()
    {
        var doomed = NewCalendar(name: "Doomed");
        var survivor = NewCalendar(name: "Survivor");
        var doomedEvent = NewEvent(doomed.Id);
        var survivorEvent = NewEvent(survivor.Id);

        Assert.True(_store.DeleteCalendar(doomed.Id));

        Assert.Null(_store.GetCalendar(doomed.Id));
        Assert.Null(_store.GetEvent(doomed.Id, doomedEvent.Id));
        Assert.NotNull(_store.GetEvent(survivor.Id, survivorEvent.Id));
    }

    [Fact]
    public void GetEvent_WithWrongCalendarId_ReturnsNull()
    {
        var ev = NewEvent(NewCalendar().Id);

        Assert.Null(_store.GetEvent(Guid.NewGuid(), ev.Id));
    }

    [Fact]
    public void DeleteEvent_WithWrongCalendarId_ReturnsFalseAndKeepsEvent()
    {
        var calendar = NewCalendar();
        var ev = NewEvent(calendar.Id);

        Assert.False(_store.DeleteEvent(Guid.NewGuid(), ev.Id));
        Assert.NotNull(_store.GetEvent(calendar.Id, ev.Id));
    }

    [Fact]
    public void UpdateAndDeleteEvent_Work()
    {
        var calendar = NewCalendar();
        var ev = NewEvent(calendar.Id);

        Assert.True(_store.UpdateEvent(ev with { Title = "Changed" }));
        Assert.Equal("Changed", _store.GetEvent(calendar.Id, ev.Id)!.Title);
        Assert.True(_store.DeleteEvent(calendar.Id, ev.Id));
        Assert.Empty(_store.ListEvents(calendar.Id));
    }
}

public class InMemoryStoreTests() : StoreContractTests(new InMemoryCalendarStore());

/// <summary>The contract tests against SQLite, plus behaviour only a real database has.</summary>
public class SqliteStoreTests : StoreContractTests, IDisposable
{
    private readonly SqliteTestDb _db;

    public SqliteStoreTests() : this(new SqliteTestDb()) { }

    private SqliteStoreTests(SqliteTestDb db) : base(db.NewStore()) => _db = db;

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Data_SurvivesANewStoreInstance()
    {
        var calendar = _db.NewStore().AddCalendar(new Calendar(Guid.NewGuid(), "alice", "Work", "desc"));

        var reloaded = _db.NewStore().GetCalendar(calendar.Id);

        Assert.Equal(calendar, reloaded);
    }

    [Fact]
    public void Event_RoundTripsRecurrenceAndUtcOffset()
    {
        var store = _db.NewStore();
        var calendar = store.AddCalendar(new Calendar(Guid.NewGuid(), "alice", "Work", null));
        var start = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.FromHours(2));
        var rule = new RecurrenceRule(RecurrenceFrequency.Weekly, 2, [DayOfWeek.Monday, DayOfWeek.Friday], Until: start.AddMonths(3));
        var ev = store.AddEvent(new CalendarEvent(Guid.NewGuid(), calendar.Id, "Gym", null, start, start.AddHours(1), rule));

        var loaded = _db.NewStore().GetEvent(calendar.Id, ev.Id)!;

        Assert.Equal(start, loaded.Start);
        Assert.Equal(TimeSpan.FromHours(2), loaded.Start.Offset); // offset is preserved, not normalised to UTC
        Assert.Equal(RecurrenceFrequency.Weekly, loaded.Recurrence!.Frequency);
        Assert.Equal(2, loaded.Recurrence.Interval);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday], loaded.Recurrence.DaysOfWeek);
        Assert.Equal(rule.Until, loaded.Recurrence.Until);
    }

    [Fact]
    public void Event_WithoutRecurrence_LoadsAsNull()
    {
        var store = _db.NewStore();
        var calendar = store.AddCalendar(new Calendar(Guid.NewGuid(), "alice", "Work", null));
        var ev = store.AddEvent(new CalendarEvent(Guid.NewGuid(), calendar.Id, "One-off", null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), null));

        Assert.Null(_db.NewStore().GetEvent(calendar.Id, ev.Id)!.Recurrence);
    }

    [Fact]
    public void AddEvent_ForUnknownCalendar_IsRejectedByForeignKey() =>
        Assert.ThrowsAny<Exception>(() => _db.NewStore().AddEvent(new CalendarEvent(
            Guid.NewGuid(), Guid.NewGuid(), "Orphan", null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), null)));

    [Fact]
    public void DeleteResource_WithBookedEvents_IsRejectedByForeignKey_AndKeepsTheEvent()
    {
        var store = _db.NewStore();
        var calendar = store.AddCalendar(new Calendar(Guid.NewGuid(), "alice", "Work", null));
        var room = store.AddResource(new Resource(Guid.NewGuid(), "Room", null, 4));
        var ev = store.AddEvent(new CalendarEvent(Guid.NewGuid(), calendar.Id, "Meeting", null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), null, room.Id));

        Assert.ThrowsAny<Exception>(() => store.DeleteResource(room.Id));

        Assert.NotNull(store.GetEvent(calendar.Id, ev.Id));
        Assert.NotNull(store.GetResource(room.Id));
    }

    [Fact]
    public void ResourceCrud_Works()
    {
        var store = _db.NewStore();
        var room = store.AddResource(new Resource(Guid.NewGuid(), "B room", null, 4));
        store.AddResource(new Resource(Guid.NewGuid(), "a room", null, null));

        Assert.Equal(["a room", "B room"], store.ListResources().Select(r => r.Name)); // case-insensitive order
        Assert.True(store.UpdateResource(room with { Capacity = 9 }));
        Assert.Equal(9, store.GetResource(room.Id)!.Capacity);
        Assert.False(store.UpdateResource(new Resource(Guid.NewGuid(), "ghost", null, null)));
        Assert.True(store.DeleteResource(room.Id));
        Assert.False(store.DeleteResource(room.Id));
    }
}

public class RequestValidatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    private static EventRequest Request(RecurrenceRule? rule = null, string? title = "Title", DateTimeOffset? end = null) =>
        new(title, null, Start, end ?? Start.AddHours(1), rule);

    [Fact]
    public void ValidEvent_HasNoErrors() => Assert.Empty(RequestValidator.Validate(Request()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EventTitle_Blank_IsRejected(string? title) =>
        Assert.Contains("title", RequestValidator.Validate(Request(title: title)).Keys);

    [Fact]
    public void EventTitle_TooLong_IsRejected() =>
        Assert.Contains("title", RequestValidator.Validate(Request(title: new string('x', 201))).Keys);

    [Fact]
    public void EventEnd_NotAfterStart_IsRejected()
    {
        Assert.Contains("end", RequestValidator.Validate(Request(end: Start)).Keys);
        Assert.Contains("end", RequestValidator.Validate(Request(end: Start.AddMinutes(-1))).Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void Recurrence_InvalidInterval_IsRejected(int interval) =>
        Assert.Contains("recurrence",
            RequestValidator.Validate(Request(new RecurrenceRule(RecurrenceFrequency.Daily, interval))).Keys);

    [Fact]
    public void Recurrence_CountAndUntil_AreMutuallyExclusive() =>
        Assert.Contains("recurrence", RequestValidator.Validate(Request(
            new RecurrenceRule(RecurrenceFrequency.Daily, Count: 2, Until: Start.AddDays(2)))).Keys);

    [Fact]
    public void Recurrence_CountBelowOne_IsRejected() =>
        Assert.Contains("recurrence",
            RequestValidator.Validate(Request(new RecurrenceRule(RecurrenceFrequency.Daily, Count: 0))).Keys);

    [Fact]
    public void Recurrence_UntilBeforeStart_IsRejected() =>
        Assert.Contains("recurrence", RequestValidator.Validate(Request(
            new RecurrenceRule(RecurrenceFrequency.Daily, Until: Start.AddDays(-1)))).Keys);

    [Fact]
    public void Recurrence_DaysOfWeekOnDaily_IsRejected() =>
        Assert.Contains("recurrence", RequestValidator.Validate(Request(
            new RecurrenceRule(RecurrenceFrequency.Daily, DaysOfWeek: [DayOfWeek.Monday]))).Keys);

    [Fact]
    public void Recurrence_InvalidFrequency_IsRejected() =>
        Assert.Contains("recurrence",
            RequestValidator.Validate(Request(new RecurrenceRule((RecurrenceFrequency)99))).Keys);

    [Fact]
    public void Recurrence_ValidWeekly_IsAccepted() =>
        Assert.Empty(RequestValidator.Validate(Request(new RecurrenceRule(
            RecurrenceFrequency.Weekly, 2, [DayOfWeek.Monday, DayOfWeek.Friday], Count: 10))));

    [Fact]
    public void Calendar_MissingOwnerOrName_IsRejected()
    {
        var errors = RequestValidator.Validate(new CreateCalendarRequest(null, " ", null));

        Assert.Contains("ownerId", errors.Keys);
        Assert.Contains("name", errors.Keys);
    }

    [Fact]
    public void Calendar_NameTooLong_IsRejected() =>
        Assert.Contains("name", RequestValidator.Validate(new UpdateCalendarRequest(new string('x', 101), null)).Keys);

    [Fact]
    public void Calendar_Valid_HasNoErrors()
    {
        Assert.Empty(RequestValidator.Validate(new CreateCalendarRequest("alice", "Work", null)));
        Assert.Empty(RequestValidator.Validate(new UpdateCalendarRequest("Work", null)));
    }
}
