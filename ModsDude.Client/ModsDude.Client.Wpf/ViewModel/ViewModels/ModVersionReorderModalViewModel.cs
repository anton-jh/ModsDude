using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

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
/// Saving writes through the same both-neighbour placement the server asserts when a version is
/// registered, one request per version that actually has to move. The alternative - one request
/// carrying the whole intended order - would need the server to accept an ordering it cannot check
/// against anything, which is exactly the assertion that stops two members silently overwriting each
/// other's answer.
/// </para>
/// </remarks>
public partial class ModVersionReorderModalViewModel : ModalViewModel
{
    private readonly Guid _repoId;
    private readonly ModKey _modId;
    private readonly IModsClient _modsClient;

    private IReadOnlyList<ModVersionKey> _serverOrder;


    public ModVersionReorderModalViewModel(
        string modName,
        Guid repoId,
        ModKey modId,
        IReadOnlyList<ModVersionKey> registeredInOrder,
        IModsClient modsClient)
    {
        ModName = modName;
        _repoId = repoId;
        _modId = modId;
        _modsClient = modsClient;
        _serverOrder = registeredInOrder;

        Order = new ModVersionOrderViewModel(registeredInOrder
            .Select(x => new ModVersionOrderEntry(x, IsMovable: true, IsUnplaceable: false)));

        // Every nudge of the list is a change to whether there is anything to save.
        Order.Entries.CollectionChanged += (_, _) => OnOrderChanged();
    }


    public string ModName { get; }

    public ModVersionOrderViewModel Order { get; }

    public string Title => "Version order";

    public string Message => "Oldest first. This is the order updates are offered in, so a mod whose "
        + "versions are the wrong way round will offer a downgrade as an update.";

    /// <summary>
    /// True once the repo's copy of the order has actually been changed, so that the page behind the
    /// dialog knows its sequence numbers are stale.
    /// </summary>
    public bool Saved { get; private set; }

    public bool CanSave => !IsSaving && !Order.Order.SequenceEqual(_serverOrder);

    /// <summary>
    /// Set when the order changed underneath the dialog. Not retried automatically: an order arrived
    /// at by a comparer can be recomputed, but this one is a person's answer to a question the client
    /// cannot re-answer on their behalf, and the question it answered was about an order that no
    /// longer exists.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;


    public bool HasNotice => string.IsNullOrWhiteSpace(Notice) is false;


    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save(CancellationToken cancellationToken)
    {
        var target = Order.Order;
        var current = _serverOrder.ToList();
        var applied = 0;

        IsSaving = true;
        Notice = null;

        try
        {
            while (true)
            {
                // A version registered or deleted while the dialog was open leaves a target order
                // that no sequence of moves can reach. Every placement would keep validating, so
                // nothing else would ever report it - it has to be noticed here.
                if (current.Count != target.Count || current.Except(target).Any())
                {
                    await ReloadAsync(DescribeDivergence(applied, "The mod's versions changed"), cancellationToken);

                    return;
                }

                if (FindFirstDifference(current, target) is not int index)
                {
                    break;
                }

                var moved = target[index];

                current.Remove(moved);

                // Everything ahead of the first difference already matches, so these two are the
                // neighbours the version lands between once it is taken out of the order.
                var after = index == 0 ? (ModVersionKey?)null : current[index - 1];
                var before = index < current.Count ? current[index] : (ModVersionKey?)null;

                var response = await _modsClient.MoveModVersionV1Async(
                    _repoId,
                    _modId.Value,
                    moved.Value,
                    new MoveModVersionRequest()
                    {
                        Placement = new ModVersionPlacement()
                        {
                            After = after?.Value,
                            Before = before?.Value
                        }
                    },
                    cancellationToken);

                applied++;
                Saved = true;

                current = [.. response.VersionIdsInOrder.Select(ModVersionKey.From)];
            }

            _serverOrder = current;

            Done = true;
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.VersionPlacementConflict)
        {
            await ReloadAsync(DescribeDivergence(applied, "Somebody else reordered this mod"), cancellationToken);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        Done = true;
    }


    private async Task ReloadAsync(string notice, CancellationToken cancellationToken)
    {
        var response = await _modsClient.GetModVersionsV1Async(_repoId, _modId.Value, cancellationToken);

        _serverOrder = [.. response.Versions.Select(x => ModVersionKey.From(x.VersionId))];

        Order.Reset(_serverOrder.Select(x => new ModVersionOrderEntry(x, IsMovable: true, IsUnplaceable: false)));

        Notice = notice;
    }

    private static string DescribeDivergence(int applied, string what)
    {
        var whatHappened = applied == 0
            ? $"{what} while this dialog was open, so nothing was saved."
            : $"{what} while this dialog was open. The first {applied} of your changes were saved before that happened.";

        return $"{whatHappened} Below is the order as it now stands - redo what you wanted and save again.";
    }

    /// <summary>
    /// The position of the first entry that is not already where it should be, or null once the two
    /// orders agree. Each move fixes one such position, so a list the user nudged twice costs two
    /// requests rather than one per version.
    /// </summary>
    private static int? FindFirstDifference(IReadOnlyList<ModVersionKey> current, IReadOnlyList<ModVersionKey> target)
    {
        for (var index = 0; index < target.Count && index < current.Count; index++)
        {
            if (current[index] != target[index])
            {
                return index;
            }
        }

        return null;
    }

    private void OnOrderChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }
}
