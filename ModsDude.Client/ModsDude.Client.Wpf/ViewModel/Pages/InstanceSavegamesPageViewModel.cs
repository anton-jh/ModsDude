using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// The instance's savegame slots: the local half of the feature, and where publishing lives.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reachable from both ends.</b> From the repo's Saves page the savegame is fixed and the instance
/// and slot are chosen; from here the instance is fixed and the slot is what everything hangs off. The
/// two are the same three verbs seen from opposite sides, exactly as activation is.
/// </para>
/// <para>
/// <b>Publish belongs here</b> because it is inherently about a slot - it takes bytes already on this
/// disk and makes a savegame of them - and because it asks nothing about the profile: the instance has
/// an active one, and that is what the first version records.
/// </para>
/// <para>
/// <b>Check-in asks nothing about the slot either.</b> The row it is clicked on is the slot, and the
/// open checkout names it. Choosing between twenty near-identical folders from memory is precisely
/// where a wrong answer publishes somebody else's farm under this save's name.
/// </para>
/// </remarks>
public partial class InstanceSavegamesPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly LocalInstance _instance;
    private readonly ISavegameService _savegameService;
    private readonly ISavegamesClient _savegamesClient;
    private readonly SavegameBindingStore _bindingStore;
    private readonly ProfileService _profileService;
    private readonly SavegameFlowService _flowService;
    private readonly IModalService _modalService;

    private readonly CancellationTokenSource _pageLifetime = new();
    private readonly CancellationToken _lifetime;

    private IReadOnlyList<SavegameSlotRowViewModel> _fetched = [];
    private string? _fetchProblem;


    public InstanceSavegamesPageViewModel(
        Repo repo,
        LocalInstance instance,
        ISavegameService savegameService,
        ISavegamesClient savegamesClient,
        SavegameBindingStore bindingStore,
        ProfileService profileService,
        SavegameFlowService flowService,
        IModalService modalService)
    {
        _repo = repo;
        _instance = instance;
        _savegameService = savegameService;
        _savegamesClient = savegamesClient;
        _bindingStore = bindingStore;
        _profileService = profileService;
        _flowService = flowService;
        _modalService = modalService;

        _lifetime = _pageLifetime.Token;

        InstanceName = instance.Name;
        CanPublish = repo.MembershipLevel >= RepoMembershipLevel.Member;

        Slots = [];
    }


    public string InstanceName { get; }

    /// <summary>Publishing and checking in both write to the repo, so both need Member.</summary>
    public bool CanPublish { get; }

    public ObservableCollection<SavegameSlotRowViewModel> Slots { get; }


    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    private string? _problem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    [ObservableProperty]
    private bool _isEmpty;

    public bool HasProblem => Problem is not null;
    public bool HasStatus => Status is not null;

    /// <summary>
    /// Which profile a publish would record. Said on the page rather than asked, so that the one thing
    /// the dialog does not ask about is still visible before it is opened.
    /// </summary>
    public string ActiveProfileText => ActiveProfileName is string name
        ? $"A save published from here is recorded against '{name}' and the mod list this instance is on."
        : "This instance follows no profile in this repo yet, so there is nothing for a published save to be recorded against.";

    public string? ActiveProfileName => _instance.ActiveProfile is ActiveProfile active && active.RepoId == _repo.Id
        ? _profileService.Profiles.FirstOrDefault(x => x.Id == active.ProfileId)?.Name
        : null;


    protected override async Task InitAsync()
    {
        _fetched = await ReadSlotsAsync(_lifetime);
    }

    protected override void OnInitCompleted()
    {
        Publish(_fetched);

        IsLoading = false;
    }

    public void Dispose()
    {
        _pageLifetime.Cancel();

        ClearRows();

        _pageLifetime.Dispose();
    }


    [RelayCommand]
    private async Task Refresh()
    {
        await ReloadAsync();
    }


    private async Task<IReadOnlyList<SavegameSlotRowViewModel>> ReadSlotsAsync(CancellationToken cancellationToken)
    {
        _fetchProblem = null;

        if (_repo.Adapter.CanSupportSavegames is false)
        {
            _fetchProblem = "This game's adapter does not manage savegames, so this instance has no slots to show.";

            return [];
        }

        // Names for the bindings this machine holds. A slot saying "'Big Valley' has been played here"
        // rather than naming a folder is the whole point, and the name lives on the server.
        var names = new Dictionary<Guid, string>();

        try
        {
            foreach (var savegame in await _savegamesClient.GetSavegamesV1Async(_repo.Id, cancellationToken))
            {
                names[savegame.Id] = savegame.Name;
            }
        }
        catch (ApiException)
        {
            // Absorbed: a slot that can only say "a checked-out savegame" is still a slot with the
            // right actions on it.
        }

        try
        {
            var slots = await _savegameService.GetSlotsAsync(_instance, cancellationToken);
            var rows = new List<SavegameSlotRowViewModel>();

            foreach (var slot in slots)
            {
                var availability = await _savegameService.ClassifySlotAsync(_instance, slot.Id, cancellationToken);
                var binding = _bindingStore.GetBindingForSlot(_instance.Id, slot.Id);

                rows.Add(new SavegameSlotRowViewModel(
                    slot,
                    availability,
                    binding?.SavegameId,
                    binding is SavegameCheckoutBinding held && names.TryGetValue(held.SavegameId, out var name) ? name : null,
                    CanPublish && ActiveProfileName is not null,
                    CanPublish));
            }

            return rows;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _fetchProblem = $"The savegame folder could not be read: {exception.Message}";

            return [];
        }
    }

    private void Publish(IReadOnlyList<SavegameSlotRowViewModel> rows)
    {
        ClearRows();

        foreach (var row in rows)
        {
            row.PublishRequested += OnPublishRequested;
            row.CheckInRequested += OnCheckInRequested;
            row.DiscardRequested += OnDiscardRequested;

            Slots.Add(row);
        }

        Problem = _fetchProblem;
        IsEmpty = Slots.Count == 0 && Problem is null;

        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(ActiveProfileText));
    }

    private void ClearRows()
    {
        foreach (var row in Slots)
        {
            row.PublishRequested -= OnPublishRequested;
            row.CheckInRequested -= OnCheckInRequested;
            row.DiscardRequested -= OnDiscardRequested;
        }

        Slots.Clear();
    }

    private async Task ReloadAsync()
    {
        IsLoading = true;

        try
        {
            Publish(await ReadSlotsAsync(_lifetime));
        }
        catch (OperationCanceledException)
        {
            // Navigated away mid-refresh.
        }
        finally
        {
            IsLoading = false;
        }
    }


    private async void OnPublishRequested(object? sender, EventArgs e)
    {
        if (sender is not SavegameSlotRowViewModel row)
        {
            return;
        }

        await RunAsync(async () =>
        {
            if (ActiveProfileName is not string profileName)
            {
                await _modalService.Show(ConfirmationDialogViewModel.Refusal(
                    "No profile is set on this instance",
                    "A savegame version records the mod list it was played on, so this instance has to be following a " +
                    "profile in this repo before anything can be published from it. Set one on the instance's own page."));

                return;
            }

            var published = await _flowService.PublishAsync(
                _instance, row.Id, row.Label, _repo.Name, profileName, _lifetime);

            if (published is null)
            {
                return;
            }

            Status = $"'{published.Name}' is in {_repo.Name}, and checked out to you. " +
                     "The save has not moved - check it in when you want somebody else to be able to take it.";

            await ReloadAsync();
        });
    }

    private async void OnCheckInRequested(object? sender, EventArgs e)
    {
        if (sender is not SavegameSlotRowViewModel row || row.SavegameId is not Guid savegameId)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var outcome = await _flowService.CheckInAsync(
                _instance, savegameId, row.SavegameName ?? "this savegame", row.Label, _lifetime);

            if (outcome.WasDeferred)
            {
                Status = "Left as it is. Your copy is still in its slot and still yours.";

                return;
            }

            if (outcome.Succeeded is false)
            {
                return;
            }

            Status = outcome.KeptPlaying
                ? $"Version {outcome.Version!.Number} is on the server. The save is still in '{row.Label}' and still yours."
                : $"Version {outcome.Version!.Number} is on the server, and '{row.Label}' is free again.";

            await ReloadAsync();
        });
    }

    private async void OnDiscardRequested(object? sender, EventArgs e)
    {
        if (sender is not SavegameSlotRowViewModel row || row.SavegameId is not Guid savegameId)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var discarded = await _flowService.DiscardAsync(
                _instance,
                savegameId,
                row.SavegameName ?? "this savegame",
                row.Label,
                row.HasUnpublishedPlay,
                _lifetime);

            if (discarded is false)
            {
                return;
            }

            Status = $"Given back without a version. '{row.Label}' is free, and the copy that was in it is in the Recycle Bin.";

            await ReloadAsync();
        });
    }

    /// <summary>
    /// The rows raise plain events rather than running commands, so a failure has no command to carry
    /// it to the global handler and has to reach the user from here.
    /// </summary>
    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // Navigated away.
        }
        catch (Exception exception)
        {
            await _modalService.Show(ConfirmationDialogViewModel.Error(
                exception as UserFriendlyException ?? UserFriendlyException.WrapUnknown(exception)));
        }
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public InstanceSavegamesPageViewModel Create(Repo repo, LocalInstance instance)
            => ActivatorUtilities.CreateInstance<InstanceSavegamesPageViewModel>(serviceProvider, repo, instance);
    }
}
