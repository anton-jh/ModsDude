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
/// The game's savegame slots: what is in each place this machine can hold a save.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reached rarely and on purpose</b>, like the rest of the game's own page. Everything an
/// ordinary evening needs - taking a save, handing it back, publishing one - is on the repo's Saves
/// list, which is where the savegames themselves are. This is the local view of the same thing: the
/// slots rather than the saves, and the state a slot can be in that no server knows about.
/// </para>
/// <para>
/// <b>Publish is not here any more.</b> It is still inherently about a slot, and it still picks one
/// out of this same list - but it is how a repo's first savegame comes into existence, so it belongs
/// where somebody looking at an empty list of savegames can find it. See
/// <see cref="RepoSavegamesPageViewModel"/>.
/// </para>
/// <para>
/// <b>Check-in asks nothing about the slot.</b> The row it is clicked on is the slot, and the
/// open checkout names it. Choosing between twenty near-identical folders from memory is precisely
/// where a wrong answer publishes somebody else's slot under this save's name.
/// </para>
/// </remarks>
public partial class GameSavegamesPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly Game _game;
    private readonly ISavegameService _savegameService;
    private readonly ISavegamesClient _savegamesClient;
    private readonly SavegameBindingStore _bindingStore;
    private readonly ProfileService _profileService;
    private readonly SavegameFlowService _flowService;
    private readonly IErrorReporter _errorReporter;

    private readonly CancellationTokenSource _pageLifetime = new();
    private readonly CancellationToken _lifetime;

    private IReadOnlyList<SavegameSlotRowViewModel> _fetched = [];
    private string? _fetchProblem;


    public GameSavegamesPageViewModel(
        Repo repo,
        Game game,
        ISavegameService savegameService,
        ISavegamesClient savegamesClient,
        SavegameBindingStore bindingStore,
        ProfileService profileService,
        SavegameFlowService flowService,
        IErrorReporter errorReporter)
    {
        _repo = repo;
        _game = game;
        _savegameService = savegameService;
        _savegamesClient = savegamesClient;
        _bindingStore = bindingStore;
        _profileService = profileService;
        _flowService = flowService;
        _errorReporter = errorReporter;

        _lifetime = _pageLifetime.Token;

        GameName = game.Name;
        CanCheckIn = repo.MembershipLevel >= RepoMembershipLevel.Member;

        Slots = [];
    }


    public string GameName { get; }

    /// <summary>Checking a save in writes a version everybody sees, so it needs Member.</summary>
    public bool CanCheckIn { get; }

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
    /// Which mod list this game is on, which is what the saves in these slots are being played
    /// against.
    /// </summary>
    /// <remarks>
    /// A statement rather than a constraint, and the reason it survives publish having moved away:
    /// a slot list is a list of saves with no mod lists in it, and which mods were beside them is the
    /// one fact about this machine that decides whether playing one damages it.
    /// </remarks>
    public string ActiveProfileText => ActiveProfileName is string name
        ? $"This game follows '{name}', so that is the mod list the saves in these slots are being played against."
        : "This game follows no profile in this repo, so nothing here says which mod list these saves are being played against.";

    public string? ActiveProfileName => _game.ActiveProfile is ActiveProfile active && active.RepoId == _repo.Id
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
            _fetchProblem = "This game's adapter does not manage savegames, so this game has no slots to show.";

            return [];
        }

        // Names for the bindings this machine holds, and - the same read, one extra question - whether
        // the repo still has each of them at all.
        var known = await ReadKnownSavegamesAsync(cancellationToken);

        try
        {
            var slots = await _savegameService.GetSlotsAsync(_game, cancellationToken);
            var rows = new List<SavegameSlotRowViewModel>();

            foreach (var slot in slots)
            {
                var availability = await _savegameService.ClassifySlotAsync(_game, slot.Ref, cancellationToken);
                var binding = _bindingStore.GetBindingForSlot(_game.Identity, slot.Ref);

                rows.Add(new SavegameSlotRowViewModel(
                    slot,
                    availability,
                    binding?.SavegameId,
                    binding is SavegameCheckoutBinding held ? known.NameOf(held.SavegameId) : null,
                    binding is SavegameCheckoutBinding bound ? known.StandingOf(bound.SavegameId) : SavegameBindingStanding.None,
                    CanCheckIn));
            }

            // Last, because they are not slots: a hold whose folder the settings no longer name has
            // no row of its own to sit in, and leaving it out would make a savegame this machine is
            // holding - and somebody else is waiting on - invisible everywhere it is shown.
            foreach (var unreachable in _savegameService.GetUnreachableHolds(_game))
            {
                rows.Add(SavegameSlotRowViewModel.ForUnreachableHold(
                    unreachable,
                    known.NameOf(unreachable.SavegameId),
                    known.StandingOf(unreachable.SavegameId)));
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

    /// <summary>
    /// Every savegame the repo has, live and archived, so a binding can be told from a binding whose
    /// savegame is gone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two lists, because archived is not deleted.</b> Archiving a savegame takes it out of the
    /// repo's list and deliberately does <em>not</em> release anybody's hold on it, so a slot holding
    /// an archived save is still holding a real thing and still checks in. Only a savegame in neither
    /// list has actually gone, and that is the case the row had no answer for: it went on reporting
    /// "checked out to you" against a savegame the server would answer 404 for, with Check in and
    /// Discard both leading straight to that 404.
    /// </para>
    /// <para>
    /// <b>A failed read is Unknown, never Gone.</b> The row for a savegame declared gone offers to
    /// forget it, so guessing that from a dropped connection would offer to throw away the only record
    /// of which save is in a slot. Absorbed the way it always was, and the row simply keeps the
    /// wording it had.
    /// </para>
    /// </remarks>
    private async Task<KnownSavegames> ReadKnownSavegamesAsync(CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        var archived = new HashSet<Guid>();

        try
        {
            foreach (var savegame in await _savegamesClient.GetSavegamesV1Async(_repo.Id, cancellationToken))
            {
                names[savegame.Id] = savegame.Name;
            }

            foreach (var savegame in await _savegamesClient.GetArchivedSavegamesV1Async(_repo.Id, cancellationToken))
            {
                names[savegame.Id] = savegame.Name;
                archived.Add(savegame.Id);
            }
        }
        catch (ApiException)
        {
            // Either read failing means nothing here can be trusted to say a savegame is missing, so
            // the whole answer is unknown rather than the half that arrived.
            return KnownSavegames.Unreadable;
        }

        return new KnownSavegames(names, archived);
    }

    private void Publish(IReadOnlyList<SavegameSlotRowViewModel> rows)
    {
        ClearRows();

        // Grouped only where the game reaches more than one savegame folder, which is what a row
        // carrying a target name means. One heading over every slot a game has would be a heading
        // repeating the page title.
        SlotGrouping.Apply(Slots, rows.Any(x => x.TargetName is not null));

        foreach (var row in rows)
        {
            row.CheckInRequested += OnCheckInRequested;
            row.DiscardRequested += OnDiscardRequested;
            row.DisconnectRequested += OnDisconnectRequested;

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
            row.CheckInRequested -= OnCheckInRequested;
            row.DiscardRequested -= OnDiscardRequested;
            row.DisconnectRequested -= OnDisconnectRequested;
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


    private async void OnCheckInRequested(object? sender, EventArgs e)
    {
        if (sender is not SavegameSlotRowViewModel row || row.SavegameId is not Guid savegameId)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var outcome = await _flowService.CheckInAsync(
                _game, savegameId, row.SavegameName ?? "this savegame", row.Label, _lifetime);

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
                _game,
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
    /// Cuts the local tie and leaves the save alone.
    /// </summary>
    /// <remarks>
    /// The only action a row whose savegame has been deleted still has, and a way out of a checkout
    /// for one whose savegame is fine - see <c>SavegameFlowService.DisconnectAsync</c>, which is where
    /// the two are told apart in the wording.
    /// </remarks>
    private async void OnDisconnectRequested(object? sender, EventArgs e)
    {
        if (sender is not SavegameSlotRowViewModel row || row.SavegameId is not Guid savegameId)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var disconnected = await _flowService.DisconnectAsync(
                _game,
                savegameId,
                row.SavegameName ?? "that savegame",
                row.Label,
                // Unknown counts as still there. The wording it produces warns about a claim that may
                // not exist, which is the harmless way round to be wrong.
                stillInRepo: row.Standing is not SavegameBindingStanding.Gone);

            if (disconnected is false)
            {
                return;
            }

            Status = $"ModsDude has let go of '{row.Label}'. Nothing on disk changed - it is an ordinary save of your own now.";

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
            await _errorReporter.ShowAsync(exception, "acting on a savegame slot");
        }
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public GameSavegamesPageViewModel Create(Repo repo, Game game)
            => ActivatorUtilities.CreateInstance<GameSavegamesPageViewModel>(serviceProvider, repo, game);
    }


    /// <param name="Readable">
    /// Whether the repo answered at all. False makes every question about a savegame
    /// <see cref="SavegameBindingStanding.Unknown"/>, which is the answer that changes nothing about
    /// a row - see <see cref="ReadKnownSavegamesAsync"/>.
    /// </param>
    private sealed record KnownSavegames(
        IReadOnlyDictionary<Guid, string> Names,
        IReadOnlySet<Guid> Archived,
        bool Readable = true)
    {
        public static KnownSavegames Unreadable { get; } =
            new(new Dictionary<Guid, string>(), new HashSet<Guid>(), Readable: false);


        public string? NameOf(Guid savegameId) => Names.GetValueOrDefault(savegameId);

        public SavegameBindingStanding StandingOf(Guid savegameId)
        {
            if (Readable is false)
            {
                return SavegameBindingStanding.Unknown;
            }

            if (Archived.Contains(savegameId))
            {
                return SavegameBindingStanding.Archived;
            }

            return Names.ContainsKey(savegameId)
                ? SavegameBindingStanding.Live
                : SavegameBindingStanding.Gone;
        }
    }
}
