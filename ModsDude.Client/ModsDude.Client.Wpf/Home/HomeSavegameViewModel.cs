using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.Home;

/// <summary>A savegame this machine has checked out, as a row on Home.</summary>
/// <param name="Name">What the savegame is called, or null until it has been read.</param>
/// <param name="RepoName">The repo it is in, or null until it has been read.</param>
public sealed record HomeSavegameViewModel(
    Game Game,
    Guid SavegameId,
    string? Name,
    string? RepoName)
{
    public string Title => Name ?? "Savegame";

    /// <summary>The game holding it and the repo it is in.</summary>
    public string Where => RepoName is null ? Game.Name : $"{Game.Name} · {RepoName}";

    /// <summary>A check-in writes the name into the slot, so it waits for the name.</summary>
    public bool CanCheckIn => Name is not null;
}
