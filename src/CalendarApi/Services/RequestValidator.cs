using CalendarApi.Models;

namespace CalendarApi.Services;

/// <summary>
/// Request validation. Each method returns a field -> messages map; an empty map means the request is valid.
/// The map plugs straight into <see cref="Results.ValidationProblem(IDictionary{string, string[]}, string?, string?, int?, string?, string?, IDictionary{string, object?}?)"/>.
/// </summary>
public static class RequestValidator
{
    private const int MaxIntervalValue = 1000;

    public static Dictionary<string, string[]> Validate(CreateCalendarRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.OwnerId))
            errors["ownerId"] = ["OwnerId is required."];
        ValidateName(request.Name, errors);
        return errors;
    }

    public static Dictionary<string, string[]> Validate(UpdateCalendarRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateName(request.Name, errors);
        return errors;
    }

    public static Dictionary<string, string[]> Validate(ResourceRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateName(request.Name, errors);
        if (request.Capacity is < 1)
            errors["capacity"] = ["Capacity must be at least 1."];
        return errors;
    }

    public static Dictionary<string, string[]> Validate(EventRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Title))
            errors["title"] = ["Title is required."];
        else if (request.Title.Length > 200)
            errors["title"] = ["Title must be 200 characters or fewer."];

        if (request.End <= request.Start)
            errors["end"] = ["End must be after Start."];

        if (request.Recurrence is { } rule)
            ValidateRecurrence(request, rule, errors);

        return errors;
    }

    private static void ValidateName(string? name, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(name))
            errors["name"] = ["Name is required."];
        else if (name.Length > 100)
            errors["name"] = ["Name must be 100 characters or fewer."];
    }

    private static void ValidateRecurrence(EventRequest request, RecurrenceRule rule, Dictionary<string, string[]> errors)
    {
        const string key = "recurrence";
        var problems = new List<string>();

        if (!Enum.IsDefined(rule.Frequency))
            problems.Add("Frequency must be Daily or Weekly.");

        if (rule.Interval is < 1 or > MaxIntervalValue)
            problems.Add($"Interval must be between 1 and {MaxIntervalValue}.");

        if (rule.Count is not null && rule.Until is not null)
            problems.Add("Specify either Count or Until, not both.");

        if (rule.Count is < 1)
            problems.Add("Count must be at least 1.");

        if (rule.Until is { } until && until < request.Start)
            problems.Add("Until must not be before Start.");

        if (rule.DaysOfWeek is { Count: > 0 } days)
        {
            if (rule.Frequency != RecurrenceFrequency.Weekly)
                problems.Add("DaysOfWeek is only valid for Weekly recurrence.");
            if (days.Any(d => !Enum.IsDefined(d)))
                problems.Add("DaysOfWeek contains an invalid day.");
        }

        if (problems.Count > 0)
            errors[key] = [.. problems];
    }
}
