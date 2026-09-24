using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// Reads the savegame list of every repo this machine holds a save in, so that a save somebody else
/// has taken over is noticed without anybody opening that repo's Saves page.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one savegame fact the drift check cannot find on this disk.</b> Everything else it reports
/// is a hash or a manifest; who holds the claim is only on the server. The check itself stays
/// offline and reads <see cref="ISavegameSightings"/>, and this is what keeps that answered for the
/// saves that matter - the ones held here - rather than only for a repo somebody happened to open.
/// </para>
/// <para>
/// <b>Costs nothing on the ordinary machine.</b> A slot is occupied by ModsDude only while a save is
/// checked out, which is usually none: with no holds this asks nothing at all, and with one it is one
/// list read per repo holding something.
/// </para>
/// </remarks>
public sealed class SavegameClaimWatch(
    IDriftCandidateSource games,
    SavegameBindingStore bindings,
    ISavegamesClient savegamesClient,
    CurrentUserService currentUserService,
    SavegameSightingCache sightings,
    ILogger<SavegameClaimWatch> logger)
{
    /// <summary>
    /// Reads the lists and records what they say.
    /// </summary>
    /// <returns>
    /// Whether anything the drift check reads about a held save - its head or its claim - changed, so
    /// the caller knows whether the notice's answer is now stale.
    /// </returns>
    public async Task<bool> RefreshAsync(CancellationToken ct)
    {
        var held = games.GetDriftCandidates()
            .SelectMany(x => bindings.GetBindings(x.Identity))
            .ToList();

        if (held.Count == 0)
        {
            return false;
        }

        var before = Describe(held);

        // Asked every time rather than remembered: it is one small read, only made while something is
        // held, and a remembered answer would outlive somebody signing in as another account.
        var currentUserId = (await currentUserService.Get(ct)).Id;

        foreach (var repoId in held.Select(x => x.RepoId).Distinct())
        {
            // Separately, so that one repo being unreadable - left, deleted, a blip - does not keep
            // the others' takeovers from being noticed.
            try
            {
                sightings.Record(repoId, await savegamesClient.GetSavegamesV1Async(repoId, ct), currentUserId);
            }
            catch (ApiException exception)
            {
                logger.LogInformation(exception, "Could not read the savegame list of repo {RepoId} to check its claims.", repoId);
            }
        }

        return Describe(held).SequenceEqual(before) is false;
    }


    private List<(int? Head, SavegameClaimSighting? Claim)> Describe(IEnumerable<SavegameCheckoutBinding> held)
    {
        return [.. held.Select(x => (
            sightings.GetHeadSnapshot(x.RepoId, x.SavegameId),
            sightings.GetClaim(x.RepoId, x.SavegameId)))];
    }
}
