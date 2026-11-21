namespace ModsDude.Server.Api.Admin;

/// <summary>How long a new invite or trust code lasts, the same choices the client offers.</summary>
public static class AdminExpiryOptions
{
    public static IReadOnlyList<AdminExpiryOption> All { get; } =
    [
        new("1 hour", 1),
        new("1 day", 24),
        new("2 days", 2 * 24),
        new("7 days", 7 * 24),
        new("30 days", 30 * 24)
    ];

    public static int DefaultHours => 7 * 24;

    public static bool IsOffered(int hours) => All.Any(x => x.Hours == hours);
}

public record AdminExpiryOption(string Label, int Hours);
