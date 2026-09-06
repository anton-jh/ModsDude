using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Which revisions a prune kept, and why - with a way to go and deal with the savegames holding
/// them.
/// </summary>
/// <remarks>
/// <para>
/// The prune deletes what it can and reports the rest, so this is never the whole answer to what
/// happened: the status line beside the list says how many went. This is the part that needs acting
/// on.
/// </para>
/// <para>
/// <b>Two reasons, and only one of them is work.</b> The head cannot be pruned at all - it is what
/// the profile pins - so that row is an explanation and nothing more. A revision a savegame was
/// played on can be freed, by deleting that savegame version, which is why those rows carry links.
/// </para>
/// </remarks>
public partial class BlockedRevisionsModalViewModel : ModalViewModel
{
    public BlockedRevisionsModalViewModel(
        string profileName,
        PruneProfileRevisionsResponse result,
        Func<Guid, Task<bool>> goToSavegame)
    {
        ProfileName = profileName;
        Deleted = result.Deleted;

        Rows = [.. result.Blocked.Select(x => new BlockedRevisionViewModel(x, OnGoTo))];

        _goToSavegame = goToSavegame;
    }


    private readonly Func<Guid, Task<bool>> _goToSavegame;


    public string ProfileName { get; }
    public int Deleted { get; }

    public ObservableCollection<BlockedRevisionViewModel> Rows { get; }

    public string Title => Rows.Count == 1
        ? "One revision was kept"
        : $"{Rows.Count} revisions were kept";

    public string Message => Deleted == 0
        ? $"Nothing was deleted from '{ProfileName}'."
        : Deleted == 1
            ? $"One revision was deleted from '{ProfileName}'. These were not:"
            : $"{Deleted} revisions were deleted from '{ProfileName}'. These were not:";


    [RelayCommand]
    private void Close() => Done = true;


    private async void OnGoTo(Guid savegameId)
    {
        // Closed first, and regardless of what navigation says: a refused one leaves the user on this
        // page, and reopening the dialog over it would be the app arguing with itself.
        Done = true;

        await _goToSavegame(savegameId);
    }
}


/// <summary>One revision that stayed, and what is holding it.</summary>
public sealed class BlockedRevisionViewModel
{
    public BlockedRevisionViewModel(BlockedRevisionDto dto, Action<Guid> goTo)
    {
        Revision = dto.Revision;
        IsHead = dto.Reason is BlockedRevisionReason.IsHead;

        Savegames = [.. dto.Savegames.Select(x => new SavegameVersionLinkViewModel(x, goTo))];

        Reason = IsHead
            ? "This is the profile's current revision. Editing the profile is what replaces it; it can never be deleted on its own."
            : Savegames.Count == 1
                ? "A savegame version was played on it. Delete that version first, and this revision can go."
                : $"{Savegames.Count} savegame versions were played on it. Delete those first, and this revision can go.";
    }


    public int Revision { get; }
    public string Label => $"Revision {Revision}";

    /// <summary>The one blocked reason nothing can be done about, which is why it reads differently.</summary>
    public bool IsHead { get; }

    public string Reason { get; }

    public IReadOnlyList<SavegameVersionLinkViewModel> Savegames { get; }

    public bool HasSavegames => Savegames.Count > 0;
}


/// <summary>One savegame version, as a link into the repo's saves list.</summary>
public partial class SavegameVersionLinkViewModel
{
    private readonly Guid _savegameId;
    private readonly Action<Guid> _goTo;


    public SavegameVersionLinkViewModel(SavegameVersionRefDto dto, Action<Guid> goTo)
    {
        _savegameId = dto.SavegameId;
        _goTo = goTo;

        Label = $"{dto.SavegameName} · version {dto.Number}";
    }


    public string Label { get; }


    [RelayCommand]
    private void Open() => _goTo(_savegameId);
}
