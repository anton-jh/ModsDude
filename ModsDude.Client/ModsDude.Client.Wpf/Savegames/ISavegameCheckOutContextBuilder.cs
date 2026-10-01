using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Wpf.Savegames;

public interface ISavegameCheckOutContextBuilder
{
    /// <summary>
    /// Everything the check-out modal needs about one game: its slots and their safety, what the mod
    /// folder would have to do, and how far the save's revision is from the profile's.
    /// </summary>
    /// <param name="nameOf">What a savegame is called, read off the caller's own list. Null where it is not in it.</param>
    Task<SavegameCheckOutContext> BuildAsync(
        Repo repo,
        SavegameDto savegame,
        Game game,
        SavegameCheckOutMode mode,
        Func<Guid, string?> nameOf,
        CancellationToken cancellationToken);
}
