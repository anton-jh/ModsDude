using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Notices;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Wpf.ViewModel.Services;

/// <summary>
/// What <see cref="NoticeBuilder"/> asks the running app, answered from the repositories the shell
/// already holds.
/// </summary>
/// <remarks>
/// Thin on purpose. Every decision about what the column says lives in the builder, where it can be
/// tested; this is the three lookups that need a signed-in client and a hydrated adapter, and
/// nothing else.
/// </remarks>
public sealed class NoticeEnvironment(
    RepoRepository repoRepository,
    GameRepository gameRepository,
    IHeldSavegames heldSavegames)
    : INoticeEnvironment
{
    public bool ReposLoaded => repoRepository.HasLoaded;


    /// <summary>
    /// The profile's own repo first, because that is the one the mod actions address. Failing that
    /// any repo about the same game, which is enough to name the folders and enough to tell a game
    /// this account can reach from one it cannot - a game reported purely for a savegame it is
    /// holding has no profile and therefore no repo of its own to name.
    /// </summary>
    public NoticeRepo? FindRepo(GameIdentity game, Guid? profileRepoId)
    {
        if (profileRepoId is Guid repoId
            && repoRepository.Repos.FirstOrDefault(x => x.Id == repoId) is Repo owner)
        {
            return new(owner.Id, owner.MembershipLevel);
        }

        return repoRepository.Repos.FirstOrDefault(x => x.Scope == game) is Repo any
            ? new(any.Id, any.MembershipLevel)
            : null;
    }

    /// <summary>
    /// Asked of the adapter, which is why this needs a repo at all. A game with one folder names
    /// nothing and every sentence reads as it did before targets existed.
    /// </summary>
    public IReadOnlyDictionary<TargetKey, string> FolderNames(GameIdentity game)
    {
        if (gameRepository.Find(game) is not Game found)
        {
            return new Dictionary<TargetKey, string>();
        }

        return repoRepository.Repos.FirstOrDefault(x => x.Scope == game) is Repo repo
            ? TargetNames.Read(found, repo.Adapter)
            : new Dictionary<TargetKey, string>();
    }

    /// <summary>
    /// The same rule <c>ModSyncService</c> resolves an apply against, so the number on the button is
    /// the number that gets installed rather than a second guess at it.
    /// </summary>
    public int? RequiredRevision(GameIdentity game, Guid profileId)
        => heldSavegames.GetRequiredRevision(game, profileId);
}
