using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Services;

/// <summary>
/// The three verbs that act on a savegame already sitting in a slot - check in, discard and publish -
/// with the dialogs they need, in one place.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="ProfileApplyService"/>, and for the same reason: check-in is reached
/// from the instance's slot list <em>and</em> from the check-out dialog's way out of a refused slot,
/// and two copies of "ask, send, resolve a stale base" would be two copies that eventually disagree
/// about what force means.
/// </para>
/// <para>
/// The modal host is taken lazily because it is the shell itself, which is composed from services that
/// may want this one - see <see cref="ProfileApplyService"/> for the cycle that closes otherwise.
/// </para>
/// </remarks>
public sealed class SavegameFlowService(ISavegameService savegames, Lazy<IModalService> modalService)
{
    /// <summary>
    /// Asks, uploads, and turns a refused base into a choice rather than an error.
    /// </summary>
    /// <returns>
    /// The version that was minted, or null where nothing was - the user backed out, the save had not
    /// changed, or they chose to look at the newer version first.
    /// </returns>
    public async Task<SavegameCheckInOutcome> CheckInAsync(
        LocalInstance instance,
        Guid savegameId,
        string savegameName,
        string slotLabel,
        CancellationToken cancellationToken)
    {
        var modal = new SavegameCheckInModalViewModel(savegameName, slotLabel);

        await modalService.Value.Show(modal);

        if (modal.Result is false)
        {
            return SavegameCheckInOutcome.Cancelled;
        }

        return await SendAsync(instance, savegameId, savegameName, modal.TrimmedLabel, modal.KeepPlaying, force: false, cancellationToken);
    }

    /// <summary>
    /// Hands the claim back without minting anything - taken by mistake, never played.
    /// </summary>
    /// <remarks>
    /// The confirmation carries what it costs, because this is the one verb with no version behind it:
    /// whatever is in the slot is gone, and the Recycle Bin is the only way back.
    /// </remarks>
    public async Task<bool> DiscardAsync(
        LocalInstance instance,
        Guid savegameId,
        string savegameName,
        string slotLabel,
        bool hasUnpublishedPlay,
        CancellationToken cancellationToken)
    {
        var consequence = hasUnpublishedPlay
            ? $"'{slotLabel}' has been played since it was downloaded, and none of that has been checked in. " +
              "It goes to the Recycle Bin and no version is minted, so the only copy of that play is one you restore by hand."
            : $"'{slotLabel}' goes to the Recycle Bin and no version is minted. The savegame goes back to being anybody's to take.";

        var modal = new ConfirmationDialogViewModel(
            $"Give '{savegameName}' back without checking it in?",
            consequence,
            hasUnpublishedPlay ? IconKind.Warning : IconKind.Question,
            "Discard it - the local copy goes to the Recycle Bin",
            "Keep it checked out");

        await modalService.Value.Show(modal);

        if (modal.Result is false)
        {
            return false;
        }

        await savegames.DiscardAsync(instance, savegameId, cancellationToken);

        return true;
    }

    /// <summary>
    /// Makes a savegame out of what is already in a slot. Never a check-in: this one names the thing
    /// being created, and the two have opposite failure modes.
    /// </summary>
    /// <returns>The savegame that was created, or null where the dialog was dismissed.</returns>
    public async Task<SavegameDto?> PublishAsync(
        LocalInstance instance,
        SavegameSlotId slot,
        string slotLabel,
        string repoName,
        string profileName,
        CancellationToken cancellationToken)
    {
        var modal = new SavegamePublishModalViewModel(slotLabel, repoName, profileName, slotLabel);

        await modalService.Value.Show(modal);

        if (modal.Result is not string name)
        {
            return null;
        }

        return await savegames.PublishAsync(instance, slot, name, modal.TrimmedLabel, cancellationToken);
    }


    /// <summary>
    /// Somebody checked in while this save was out. Both answers are safe and neither destroys
    /// anything: a forced check-in becomes the head with the version it was built on recorded beside
    /// it, so the fork ends up in the record rather than one side of it being lost.
    /// </summary>
    private async Task<SavegameCheckInOutcome> SendAsync(
        LocalInstance instance,
        Guid savegameId,
        string savegameName,
        string? label,
        bool keepPlaying,
        bool force,
        CancellationToken cancellationToken)
    {
        try
        {
            var version = await savegames.CheckInAsync(instance, savegameId, label, keepPlaying, force, cancellationToken);

            return SavegameCheckInOutcome.CheckedIn(version, keepPlaying);
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.SavegameVersionStale)
        {
            var choice = new ConfirmationDialogViewModel(
                $"Somebody else checked '{savegameName}' in",
                "Your save was built on an older version. Checking yours in anyway records it as the newest one, with " +
                "theirs named beside it and still in the history - nothing is deleted either way. Leaving it alone keeps " +
                "your copy exactly where it is, so you can look at theirs first and decide.",
                IconKind.Warning,
                "Check mine in anyway - theirs stays in the history",
                "Leave mine alone for now");

            await modalService.Value.Show(choice);

            if (choice.Result is false)
            {
                return SavegameCheckInOutcome.Deferred;
            }

            return await SendAsync(instance, savegameId, savegameName, label, keepPlaying, force: true, cancellationToken);
        }
        catch (UserFriendlyException exception)
        {
            await modalService.Value.Show(ConfirmationDialogViewModel.Error(exception));

            return SavegameCheckInOutcome.Cancelled;
        }
    }
}


/// <summary>
/// What a check-in ended up doing. Three outcomes rather than a nullable version, because "you backed
/// out" and "you chose to look at theirs first" leave the caller with different things to say.
/// </summary>
public sealed record SavegameCheckInOutcome(SavegameVersionDto? Version, bool KeptPlaying, bool WasDeferred)
{
    public static SavegameCheckInOutcome Cancelled { get; } = new(null, false, false);

    /// <summary>The base was stale and the user chose to look at the newer version first.</summary>
    public static SavegameCheckInOutcome Deferred { get; } = new(null, false, true);

    public static SavegameCheckInOutcome CheckedIn(SavegameVersionDto version, bool keptPlaying)
        => new(version, keptPlaying, false);

    public bool Succeeded => Version is not null;

    /// <summary>Whether the slot is now free, which is what the caller has to re-read the disk about.</summary>
    public bool ReleasedTheSlot => Succeeded && KeptPlaying is false;
}
