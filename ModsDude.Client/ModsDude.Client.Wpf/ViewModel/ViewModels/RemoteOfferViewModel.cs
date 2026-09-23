using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using System.Windows.Input;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// A newer version of a mod that a remote source has, as the chip on the mod's row that goes and gets
/// it. There is nothing to pin behind it - see <see cref="Core.Models.ModSourceKind.Remote"/> - so all
/// it can do is open the page the file is downloaded from.
/// </summary>
public sealed class RemoteOfferViewModel(RemoteModOffer offer, string sourceName, Action<RemoteModOffer> open)
{
    public RemoteModOffer Offer { get; } = offer;

    public string Text { get; } = $"{sourceName} {offer.VersionId.Value}";

    public string Tooltip { get; } =
        $"{sourceName} has version {offer.VersionId.Value} of {offer.Title}, newer than anything here.\n" +
        $"Opens its page to download it. Once the file is in Downloads, rescan and it can be added like any other version.";

    public ICommand Open { get; } = new RelayCommand(() => open(offer));
}
