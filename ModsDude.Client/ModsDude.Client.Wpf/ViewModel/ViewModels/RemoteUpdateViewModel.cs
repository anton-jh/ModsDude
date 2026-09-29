using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using System.Windows.Input;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// A newer version of a mod that is online, as the chip on the mod's row that goes and gets it. There is
/// nothing to pin behind it, so all it can do is open the page the file is downloaded from.
/// </summary>
public sealed class RemoteUpdateViewModel(RemoteModVersion version, string providerName, Action<RemoteModVersion> open)
{
    public RemoteModVersion Version { get; } = version;

    public string Text { get; } = $"{providerName} {version.VersionId.Value}";

    public string Tooltip { get; } =
        $"{providerName} has version {version.VersionId.Value} of {version.Title}, newer than anything here.\n" +
        "Opens its page to download it. Once the file is in Downloads, rescan and it can be added like any other version.";

    public ICommand Open { get; } = new RelayCommand(() => open(version));
}
