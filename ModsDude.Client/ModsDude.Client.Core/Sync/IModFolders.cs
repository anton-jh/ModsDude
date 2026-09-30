using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Sync;

/// <summary>The mod folders on this machine, which is what eviction needs to know to spare them.</summary>
/// <remarks>
/// An interface rather than <see cref="GameRepository"/> itself, so the sync engine depends
/// on the one fact it uses and can be exercised without a real <c>state.json</c>.
/// </remarks>
public interface IModFolders
{
    IReadOnlyList<GameModFolder> GetAll();
}

/// <summary>
/// One mod folder on this machine, and which of which game's targets reaches it.
/// </summary>
/// <remarks>
/// One entry per folder, so a game with three targets appears three times: eviction has to spare
/// every folder, and the target is how each one's manifest is found.
/// </remarks>
public sealed record GameModFolder(ModTargetRef Target, string ModFolder);
