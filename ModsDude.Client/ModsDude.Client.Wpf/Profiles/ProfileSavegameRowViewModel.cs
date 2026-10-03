using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Wpf.Savegames;

namespace ModsDude.Client.Wpf.Profiles;

/// <summary>
/// One of a profile's savegames on its overview: a name that leads to it on the Saves list, when it
/// was last played, and who has it.
/// </summary>
public partial class ProfileSavegameRowViewModel(SavegameDto savegame, DateTimeOffset now, Func<Guid, Task> open)
{
    public string Name => savegame.Name;

    public string LastPlayedText => savegame.Head is SavegameSnapshotDto head
        ? SavegameWording.Ago(head.Created, now)
        : "Never";

    public string HolderText => savegame.Checkout is { Status: not SavegameCheckoutStatus.Ended } checkout
        ? checkout.User.DisplayName
        : "Available";


    [RelayCommand]
    private Task Open() => open(savegame.Id);
}
