using System.Globalization;

namespace FeedbackIntel.Core.Configuration;

/// <summary>
/// The <c>Backfill</c> settings: an optional date range for one manual run (FR-ING-06). They
/// never appear in <c>config/pipeline.json</c>; the workflow's backfill inputs set them through
/// the <c>Backfill__From</c> and <c>Backfill__To</c> environment variables.
/// </summary>
public sealed class BackfillOptions
{
    /// <summary>The date format both inputs use.</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>The first creation date to re-fetch, or empty for a normal run.</summary>
    public string From { get; set; } = "";

    /// <summary>The last creation date to re-fetch. Empty means up to today.</summary>
    public string To { get; set; } = "";

    /// <summary>Returns a problem with the dates, or null if they are valid or not set.</summary>
    public string? FindProblem()
    {
        if (string.IsNullOrWhiteSpace(From))
        {
            return string.IsNullOrWhiteSpace(To) ? null : "Backfill:To needs a Backfill:From date as well.";
        }

        if (!TryParse(From, out var from) || (!string.IsNullOrWhiteSpace(To) && !TryParse(To, out _)))
        {
            return $"Backfill:From and Backfill:To must be dates in the form {DateFormat}.";
        }

        if (!string.IsNullOrWhiteSpace(To) && TryParse(To, out var to) && to < from)
        {
            return "Backfill:To must not be before Backfill:From.";
        }

        return null;
    }

    /// <summary>The range to re-fetch, or null for a normal run. Call only after <see cref="FindProblem"/> passes.</summary>
    /// <param name="today">The current date, used when no end date is given.</param>
    public BackfillRange? ToRange(DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(From))
        {
            return null;
        }

        var from = Parse(From);
        var to = string.IsNullOrWhiteSpace(To) ? today : Parse(To);
        return new BackfillRange(from, to);
    }

    private static bool TryParse(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static DateOnly Parse(string value) =>
        DateOnly.ParseExact(value.Trim(), DateFormat, CultureInfo.InvariantCulture);
}

/// <summary>Issues created from <paramref name="From"/> to <paramref name="To"/>, both days included (UTC).</summary>
public sealed record BackfillRange(DateOnly From, DateOnly To)
{
    /// <summary>The start of the first day (UTC).</summary>
    public DateTime Start => From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    /// <summary>The start of the day after the last day (UTC), so the last day is included.</summary>
    public DateTime End => To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    /// <summary>Whether an issue created at this time falls in the range.</summary>
    public bool Contains(DateTime createdAt) => createdAt >= Start && createdAt < End;
}
