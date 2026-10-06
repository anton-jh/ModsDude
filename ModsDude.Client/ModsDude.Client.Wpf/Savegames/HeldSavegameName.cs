namespace ModsDude.Client.Wpf.Savegames;

/// <summary>
/// What a savegame held on this machine is called, and which repo it is in where that is not the
/// repo being looked at.
/// </summary>
/// <param name="Name">Its name, or null where its record could not be read.</param>
/// <param name="OtherRepoName">The repo it is in, where that is another repo than the one in view.</param>
public sealed record HeldSavegameName(string? Name, string? OtherRepoName)
{
    public static HeldSavegameName Unknown { get; } = new(null, null);

    /// <summary>How a sentence refers to it.</summary>
    public string Quoted => Name is null
        ? "the savegame checked out here"
        : OtherRepoName is null ? $"'{Name}'" : $"'{Name}' ({OtherRepoName})";
}
