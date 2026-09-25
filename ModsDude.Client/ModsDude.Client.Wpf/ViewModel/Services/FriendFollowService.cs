using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Wpf.ViewModel.Services;

/// <summary>
/// Puts this machine's game on what a friend is on - the one gesture behind every "Use this
/// profile" button, on Home, on a repo's overview and on a notice.
/// </summary>
/// <remarks>
/// <para>
/// <b>An ordinary activation</b>, through <see cref="ProfileApplyService"/> like every other: a held
/// savegame refuses it, the plan is shown before the previous profile's mods come out, and the intent
/// is recorded before any file moves.
/// </para>
/// <para>
/// <b>The revision only where the friend is held on one.</b> A friend who activated the profile is on
/// head, whatever number head is, and following them follows head - so the game here keeps moving
/// with the profile. A friend on a past savegame is on that savegame's revision, and joining them means
/// being on it too; that one is pinned, and activating the profile again is the way back to head.
/// </para>
/// </remarks>
public sealed class FriendFollowService(
    RepoRepository repoRepository,
    GameRepository gameRepository,
    ProfileApplyService applyService,
    DriftMonitor driftMonitor)
{
    /// <returns>What to tell the user, and how loudly.</returns>
    public async Task<(string Message, ToastSeverity Severity)> FollowAsync(GameActivityDto activity, CancellationToken cancellationToken)
    {
        if (repoRepository.Repos.FirstOrDefault(x => x.Id == activity.RepoId) is not Repo repo)
        {
            return ("The repo that profile is in has not loaded yet. Try again in a moment.", ToastSeverity.Warning);
        }

        if (FriendActivityRules.ParseGame(activity.Game) is not GameIdentity identity
            || gameRepository.Find(identity) is not Game game)
        {
            return ("That game is not connected on this machine.", ToastSeverity.Warning);
        }

        var outcome = await applyService.ActivateAsync(
            repo,
            game,
            activity.ProfileId,
            activity.ProfileName,
            // Following somebody moves the game off whatever it was on, so the plan is shown first -
            // the same reason the profile page shows it.
            confirmPlan: true,
            progress: null,
            cancellationToken,
            revision: activity.PinnedRevision,
            pinRevision: activity.PinnedRevision is not null);

        await driftMonitor.CheckAsync();

        return (outcome.Message, outcome.ToastSeverity);
    }
}
