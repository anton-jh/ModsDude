using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Reordering one mod's registered versions by hand - the backstop for an order that is wrong for
/// reasons optimistic concurrency cannot catch, such as a comparer that guessed badly or an
/// arbitration someone regrets.
/// </summary>
/// <remarks>
/// The same operation the arbitration dialog performs, over the same control, with every version
/// movable because every one of them is already registered.
/// <para>
/// <b>Saving is not wired up.</b> A new order has to be written through the same both-neighbour
/// placement the server asserts when a version is registered, and there is no endpoint that moves an
/// already-registered version - the only placement the API accepts arrives with a registration.
/// Rather than pretend otherwise, the dialog reads the current order and says why it cannot write
/// one back. See docs/PLAN.md Phase 2.
/// </para>
/// </remarks>
public partial class ModVersionReorderModalViewModel : ModalViewModel
{
    public ModVersionReorderModalViewModel(string modName, IReadOnlyList<ModVersionKey> registeredInOrder)
    {
        ModName = modName;
        Order = new ModVersionOrderViewModel(registeredInOrder
            .Select(x => new ModVersionOrderEntry(x, IsMovable: true, IsUnplaceable: false)));
    }


    public string ModName { get; }

    public ModVersionOrderViewModel Order { get; }

    public string Title => "Version order";

    public string Message => "Oldest first. This is the order updates are offered in, so a mod whose "
        + "versions are the wrong way round will offer a downgrade as an update.";

    public bool CanSave => false;

    public string SaveUnavailableReason =>
        "Saving a new order needs a server endpoint that moves a registered version, which does not exist yet.";


    [RelayCommand]
    private void Close()
    {
        Done = true;
    }
}
