using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Asks, once per import, where the versions the comparer could not place go.
/// </summary>
/// <remarks>
/// <para>
/// One dialog for every ambiguous mod rather than one per mod, and never one per unordered pair: the
/// import computes the whole selection's ordering up front, so everything the comparer settled is
/// already registering by the time this appears and only the remainder is on screen.
/// </para>
/// <para>
/// Cancelling skips the mods listed here and lets the rest of the import finish, which is why the
/// dismissing button says so rather than saying "cancel". One unorderable mod is one mod's problem;
/// losing a two-thousand-mod batch over it is not a trade anybody would make.
/// See docs/09-mod-catalog.md#when-abstention-forces-a-prompt.
/// </para>
/// </remarks>
public partial class ModVersionArbitrationModalViewModel : ModalViewModel
{
    public ModVersionArbitrationModalViewModel(IReadOnlyList<ModVersionArbitrationItem> items)
    {
        Mods = [.. items.Select(x => new ModVersionArbitrationItemViewModel(x))];
    }


    public ObservableCollection<ModVersionArbitrationItemViewModel> Mods { get; }

    public string Title => Mods.Count == 1
        ? "Where does this version go?"
        : $"Where do these {Mods.Count} mods' versions go?";

    public string Message =>
        "The version strings do not say which release came first. Put each floating version where it "
        + "belongs; the ones already in the repo cannot move, because registering only ever inserts "
        + "around them.";

    public string SkipMessage => Mods.Count == 1
        ? "Skipping leaves this mod unregistered. The rest of the import carries on, and this one can be imported again later."
        : $"Skipping leaves these {Mods.Count} mods unregistered. The rest of the import carries on, and they can be imported again later.";

    /// <summary>
    /// The intended final order per mod, or null where the user declined to say. A null answer skips
    /// exactly the mods this dialog was asking about.
    /// </summary>
    public IReadOnlyDictionary<ModKey, IReadOnlyList<ModVersionKey>>? Result { get; private set; }


    [RelayCommand]
    private void Confirm()
    {
        Result = Mods.ToDictionary(x => x.ModId, x => x.Order.Order);
        Done = true;
    }

    [RelayCommand]
    private void Skip()
    {
        Result = null;
        Done = true;
    }
}


public class ModVersionArbitrationItemViewModel
{
    public ModVersionArbitrationItemViewModel(ModVersionArbitrationItem item)
    {
        ModId = item.ModId;

        // Seeded with the order that was derived, so a list the comparer mostly settled arrives
        // mostly right and the user only has to look at what is marked.
        Order = new ModVersionOrderViewModel(item.Versions.Select(x => new ModVersionOrderEntry(
            x.VersionId,
            IsMovable: x.IsIncoming,
            IsUnplaceable: x.IsUnplaceable,
            Note: x.IsIncoming ? "importing" : "in repo")));
    }


    public ModKey ModId { get; }

    public string Name => ModId.Value;

    public ModVersionOrderViewModel Order { get; }
}
