using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Users;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// Reads the savegame list of every repo this machine holds a save in, so that a save somebody else
/// has taken over is noticed without anybody opening that repo's Saves page.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one savegame fact the drift check cannot find on this disk.</b> Everything else it reports
/// is a hash or a manifest; who holds the claim is only on the server. The check reads it from
/// <see cref="ISavegameStore"/>, and this is what keeps that answered for the saves that matter - the
/// ones held here - rather than only for a repo somebody happened to open.
/// </para>
/// <para>
/// <b>Costs nothing on the ordinary machine.</b> A slot is occupied by ModsDude only while a save is
/// checked out, which is usually none: with no holds this asks nothing at all, and with one it is one
/// list read per repo holding something.
/// </para>
/// </remarks>
public sealed class SavegameClaimWatch(
    IDriftCandidateSource games,
    ISavegameBindingStore bindings,
    ISavegameStore store,
    ICurrentUserStore currentUser,
    ILogger<SavegameClaimWatch> logger) : ISavegameClaimWatch
{
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

        // Before the reads: whose a claim is cannot be told without it.
        await currentUser.GetAsync(ct);

        foreach (var repoId in held.Select(x => x.RepoId).Distinct())
        {
            // Separately, so that one repo being unreadable - left, deleted, a blip - does not keep
            // the others' takeovers from being noticed.
            try
            {
                await store.RefreshAsync(repoId, ct);
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
            store.GetHeadSnapshot(x.RepoId, x.SavegameId),
            store.GetClaim(x.RepoId, x.SavegameId)))];
    }
}
