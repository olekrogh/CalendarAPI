# Calendar API

A .NET 10 minimal API for managing multiple users' calendars, events (including daily and weekly recurrence) and
bookable resources such as meeting rooms.

## Features

- **Multiple calendars per user**: each calendar has an `ownerId`; a user can own as many as they like.
- **Full CRUD** for calendars, events and resources.
- **Recurring events**: daily or weekly, with an interval, chosen weekdays, and an end by count or date.
- **Resources (meeting rooms)**: events can book a resource, and double-booking is rejected (including clashes
  between recurring series).
- **OpenAPI + Swagger UI** in Development.

## Getting started

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet run --project src/CalendarApi     # http://localhost:5063
dotnet test                              # unit + integration tests
```

- Swagger UI: <http://localhost:5063/swagger>
- OpenAPI document: <http://localhost:5063/openapi/v1.json>
- Ready-made requests: [`src/CalendarApi/CalendarApi.http`](src/CalendarApi/CalendarApi.http)
  (VS, VS Code REST Client, Rider). Run them top to bottom; later requests reuse earlier IDs.

Data is held **in memory** and is lost when the app restarts.

## Endpoints

### Calendars

| Method | Path | Description |
|---|---|---|
| POST | `/api/calendars` | Create a calendar |
| GET | `/api/calendars?ownerId=` | List calendars, optionally for one user |
| GET | `/api/calendars/{id}` | Get a calendar |
| PUT | `/api/calendars/{id}` | Rename / change description (owner cannot change) |
| DELETE | `/api/calendars/{id}` | Delete a calendar and all its events |

### Events

| Method | Path | Description |
|---|---|---|
| POST | `/api/calendars/{calendarId}/events` | Create an event |
| GET | `/api/calendars/{calendarId}/events` | List stored events (a series appears once) |
| GET | `/api/calendars/{calendarId}/events/{eventId}` | Get an event |
| PUT | `/api/calendars/{calendarId}/events/{eventId}` | Replace an event (changes the whole series) |
| DELETE | `/api/calendars/{calendarId}/events/{eventId}` | Delete an event (the whole series) |
| GET | `/api/calendars/{calendarId}/occurrences?from=&to=` | Expanded occurrences overlapping the window |

### Resources

| Method | Path | Description |
|---|---|---|
| POST | `/api/resources` | Create a resource |
| GET | `/api/resources` | List resources |
| GET | `/api/resources/{id}` | Get a resource |
| PUT | `/api/resources/{id}` | Update a resource |
| DELETE | `/api/resources/{id}` | Delete a resource (409 while events still book it) |
| GET | `/api/resources/{id}/occurrences?from=&to=` | All bookings of the resource across every calendar |

### Status codes

| Code | Meaning |
|---|---|
| 200 / 201 / 204 | Success (read or update / created / deleted) |
| 400 | Validation failed; the body lists errors per field |
| 404 | Calendar, event or resource not found |
| 409 | Resource already booked for that time, or resource still has bookings |

## Examples

Create a calendar and a room:

```http
POST /api/calendars
{ "ownerId": "alice", "name": "Work" }

POST /api/resources
{ "name": "Boardroom", "capacity": 10 }
```

A stand-up every Monday, Wednesday and Friday until 31 March, booking the room:

```http
POST /api/calendars/{calendarId}/events
{
  "title": "Stand-up",
  "start": "2026-01-05T09:00:00Z",
  "end": "2026-01-05T09:30:00Z",
  "resourceId": "{roomId}",
  "recurrence": {
    "frequency": "Weekly",
    "interval": 1,
    "daysOfWeek": ["Monday", "Wednesday", "Friday"],
    "until": "2026-03-31T00:00:00Z"
  }
}
```

Booking the room at an overlapping time returns `409`:

```json
{
  "message": "The resource is already booked for an overlapping time.",
  "conflictingEventId": "…",
  "conflictingCalendarId": "…"
}
```

## Recurrence rules

| Field | Notes |
|---|---|
| `frequency` | `Daily` or `Weekly` |
| `interval` | Repeat every N days/weeks, 1 to 1000 (default 1) |
| `daysOfWeek` | Weekly only. Defaults to the weekday of `start`. Weeks run Monday to Sunday. |
| `count` | Total number of occurrences. Cannot be combined with `until`. |
| `until` | Inclusive end of the series. Cannot be combined with `count`. |

With neither `count` nor `until` the series is open-ended.

## Behaviour to be aware of

- Occurrences are computed on request, never stored. Queries return at most 5,000 occurrences.
- Recurrence keeps the start's UTC offset, so there is no daylight-saving adjustment.
- Open-ended series are checked for resource clashes over 730 days from the new event's start.
- Booking checks use an in-process lock, so they are only safe within a single running instance.
- There is no authentication, and no way to edit or skip a single occurrence of a series.

## Project layout

```
src/CalendarApi/
  Endpoints/   Route definitions (calendars, events, resources)
  Models/      Records and request bodies
  Services/    RecurrenceExpander, BookingService, ICalendarStore (in-memory), RequestValidator
tests/CalendarApi.Tests/   Unit tests (recurrence, store, validation, booking) and HTTP integration tests
```

`ICalendarStore` is the persistence seam: replace `InMemoryCalendarStore` to use a database.
