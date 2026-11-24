namespace ModsDude.Server.Api.Admin;

/// <summary>One page of a list, numbered from 1.</summary>
public sealed record AdminPage<T>(IReadOnlyList<T> Items, int Number, int Count, int Total)
{
    public const int Size = 50;

    public bool HasPrevious => Number > 1;
    public bool HasNext => Number < Count;


    /// <param name="number">The page asked for; out of range is moved to the nearest page there is.</param>
    public static AdminPage<T> Of(IReadOnlyList<T> all, int number)
    {
        var count = Math.Max(1, (all.Count + Size - 1) / Size);
        var clamped = Math.Clamp(number, 1, count);

        return new AdminPage<T>([.. all.Skip((clamped - 1) * Size).Take(Size)], clamped, count, all.Count);
    }
}
