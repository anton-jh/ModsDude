namespace ModsDude.Client.Core.GameAdapters;

/// <summary>
/// A change an adapter wants made to one of a game's files, which the engine carries out.
/// </summary>
/// <param name="RelativePath">Relative to the folder the edit is asked about. May climb out of it with <c>..</c>, never be rooted.</param>
/// <param name="Transform">
/// Pure: the file's current bytes (null where it does not exist) to the bytes it should hold, or null
/// to leave it as it is. Applying it to its own output changes nothing, and it keeps whatever it does
/// not manage.
/// </param>
public sealed record GameFileEdit(string RelativePath, Func<byte[]?, byte[]?> Transform)
{
    /// <summary>Whether the content being replaced goes to the Recycle Bin first.</summary>
    public bool RecycleReplaced { get; init; }
}
