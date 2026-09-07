using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// What the repo has put away: its archived profiles and savegames, with the two things that can be
/// done to one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Open to everybody, actionable by an admin.</b> A profile that quietly vanished from the
/// sidebar has to be explainable to whoever noticed, so the page is not gated - only Restore and
/// Delete are. Hiding the archive would turn "where did it go" into a question nobody but an admin
/// could answer.
/// </para>
/// <para>
/// <b>The archived-at timestamp is on every row, and it is load-bearing.</b> Archived things do not
/// hold their names, so several of them may be called the same thing; when they were put away is
/// the only thing telling them apart.
/// </para>
/// <para>
/// Profiles and savegames share one page because they share one question - what has this repo put
/// away - and two pages called Archive under one repo would be two places to look.
/// </para>
/// </remarks>
public partial class RepoArchivePageViewModel : PageViewModel
{
    private readonly Repo _repo;
    private readonly ProfileService _profileService;
    private readonly LocalInstanceRepository _localInstances;
    private readonly ISavegamesClient _savegamesClient;
    private readonly IModalService _modalService;
    private readonly IErrorReporter _errorReporter;

    private readonly CancellationTokenSource _lifetime = new();

    private Guid? _highlightOnce;

    private IReadOnlyList<ProfileDto> _fetchedProfiles = [];
    private IReadOnlyList<SavegameDto> _fetchedSavegames = [];


    public RepoArchivePageViewModel(
        Repo repo,
        ProfileService profileService,
        LocalInstanceRepository localInstances,
        ISavegamesClient savegamesClient,
        IModalService modalService,
        IErrorReporter errorReporter)
    {
        _repo = repo;
        _profileService = profileService;
        _localInstances = localInstances;
        _savegamesClient = savegamesClient;
        _modalService = modalService;
        _errorReporter = errorReporter;

        // Restoring is the same level as the archiving it undoes - curating the repo's profiles and
        // saves is what a Member is for. Losing one for good is not.
        CanRestore = repo.MembershipLevel >= RepoMembershipLevel.Member;
        CanDelete = repo.MembershipLevel >= RepoMembershipLevel.Admin;

        RepoName = repo.Name;
    }


    /// <summary>
    /// Which row a link asked to be shown, used once and then forgotten.
    /// </summary>
    /// <remarks>
    /// A link into an archived savegame lands here rather than on the saves list, because an
    /// archived savegame has no row there - the archive row <em>is</em> the savegame, so picking it
    /// out is what "take them to it" means for one.
    /// </remarks>
    public void HighlightOnArrival(Guid? id) => _highlightOnce = id;

    public string RepoName { get; }

    /// <summary>Whether Restore is offered. Reading the archive is open to everybody.</summary>
    public bool CanRestore { get; }

    /// <summary>Whether permanent deletion is offered.</summary>
    public bool CanDelete { get; }

    public ObservableCollection<ArchivedItemViewModel> Profiles { get; } = [];
    public ObservableCollection<ArchivedItemViewModel> Savegames { get; } = [];

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isWorking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;


    public bool HasStatus => Status is not null;

    public bool HasProfiles => Profiles.Count > 0;
    public bool HasSavegames => Savegames.Count > 0;

    public bool IsEmpty => IsLoading is false && HasProfiles is false && HasSavegames is false;


    /// <summary>
    /// The first load runs off the UI thread, so it only fetches. The rows are filled in
    /// <see cref="OnInitCompleted"/>: both lists are bound, and a bound collection refuses to be
    /// changed from any thread but the dispatcher's.
    /// </summary>
    protected override async Task InitAsync()
    {
        (_fetchedProfiles, _fetchedSavegames) = await FetchAsync();
    }

    protected override void OnInitCompleted()
    {
        Publish(_fetchedProfiles, _fetchedSavegames);

        IsLoading = false;
        Notify();
    }

    /// <summary>
    /// Reported here rather than rethrown, so the modal names what was being read - and so a page
    /// that failed to load stops claiming it is still loading.
    /// </summary>
    protected override void OnInitFailed(Exception exception)
    {
        IsLoading = false;
        Notify();

        if (exception is OperationCanceledException)
        {
            // Navigated away.
            return;
        }

        _ = _errorReporter.ShowAsync(exception, "reading the repo's archive");
    }

    [RelayCommand]
    private async Task Refresh() => await ReloadAsync();


    private async Task<(IReadOnlyList<ProfileDto> Profiles, IReadOnlyList<SavegameDto> Savegames)> FetchAsync()
    {
        var profiles = await _profileService.GetArchivedProfiles(_repo.Id, _lifetime.Token);
        var savegames = await _savegamesClient.GetArchivedSavegamesV1Async(_repo.Id, _lifetime.Token);

        return (profiles, [.. savegames]);
    }

    /// <summary>Fills the two lists. Dispatcher thread only.</summary>
    private void Publish(IReadOnlyList<ProfileDto> profiles, IReadOnlyList<SavegameDto> savegames)
    {
        Profiles.Clear();
        Savegames.Clear();

        foreach (var profile in profiles)
        {
            Profiles.Add(new ArchivedItemViewModel(
                profile.Id, profile.Name, profile.ArchivedAt, CanRestore, CanDelete, RestoreProfileAsync, DeleteProfileAsync)
            {
                IsHighlighted = profile.Id == _highlightOnce
            });
        }

        foreach (var savegame in savegames)
        {
            Savegames.Add(new ArchivedItemViewModel(
                savegame.Id, savegame.Name, savegame.ArchivedAt, CanRestore, CanDelete, RestoreSavegameAsync, DeleteSavegameAsync)
            {
                IsHighlighted = savegame.Id == _highlightOnce
            });
        }

        // Used once: a refresh later should not keep re-pointing at where somebody arrived.
        _highlightOnce = null;
    }

    /// <summary>
    /// Rereads the archive from somewhere the user is already standing - a refresh, or the tail of a
    /// restore or a delete. Those all run on the dispatcher, so this one publishes where it stands.
    /// </summary>
    private async Task ReloadAsync()
    {
        IsLoading = true;

        try
        {
            var (profiles, savegames) = await FetchAsync();

            Publish(profiles, savegames);
        }
        catch (OperationCanceledException)
        {
            // Navigated away.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "reading the repo's archive");
        }
        finally
        {
            IsLoading = false;
            Notify();
        }
    }

    /// <summary>
    /// Restores, and asks for another name where the old one has been taken since.
    /// </summary>
    /// <remarks>
    /// The clash is discovered by trying rather than by checking first: the check would be a second
    /// copy of a rule the server and its filtered index already own, and it would still be racing.
    /// </remarks>
    private async Task RestoreProfileAsync(ArchivedItemViewModel item)
    {
        await RunAsync(async name =>
        {
            await _profileService.RestoreProfile(_repo.Id, item.Id, name, _lifetime.Token);

            Status = $"'{name ?? item.Name}' is back in the sidebar.";
        }, item, "profile");
    }

    private async Task RestoreSavegameAsync(ArchivedItemViewModel item)
    {
        await RunAsync(async name =>
        {
            await _savegamesClient.RestoreSavegameV1Async(
                _repo.Id, item.Id, new RestoreRequest { Name = name }, _lifetime.Token);

            Status = $"'{name ?? item.Name}' is back in the repo's saves.";
        }, item, "savegame");
    }

    /// <summary>
    /// One restore, retried once under a new name if the first attempt hit the clash the archive
    /// deferred.
    /// </summary>
    private async Task RunAsync(Func<string?, Task> restore, ArchivedItemViewModel item, string what)
    {
        IsWorking = true;
        Status = null;

        try
        {
            try
            {
                await restore(null);
            }
            catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.NameTaken)
            {
                if (await AskForNameAsync(item, what) is not string renamed)
                {
                    return;
                }

                await restore(renamed);
            }
            catch (Core.Exceptions.UserFriendlyException exception) when (exception.Message == "Name taken")
            {
                if (await AskForNameAsync(item, what) is not string renamed)
                {
                    return;
                }

                await restore(renamed);
            }

            await ReloadAsync();
        }
        catch (OperationCanceledException)
        {
            // Navigated away.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, $"restoring an archived {what}");
        }
        finally
        {
            IsWorking = false;
        }
    }

    private async Task<string?> AskForNameAsync(ArchivedItemViewModel item, string what)
    {
        var modal = new RenameModalViewModel(
            "That name is taken",
            $"Something else in this repo is called '{item.Name}' now. Give this {what} another name to bring it back.",
            $"{item.Name} (restored)");

        await _modalService.Show(modal);

        return modal.Result;
    }

    private async Task DeleteProfileAsync(ArchivedItemViewModel item)
    {
        if (await ConfirmDeleteAsync(item) is false)
        {
            return;
        }

        IsWorking = true;

        try
        {
            await _profileService.DeleteProfile(_repo.Id, item.Id, _lifetime.Token);

            // The profile is gone, so no instance can still be pointed at it. Local state, which the
            // server has no idea about: an instance whose active profile is a dangling id reports
            // drift against a mod list nobody can read. An *archived* profile is still tracked -
            // this is the deletion letting go, not the archiving.
            _localInstances.StopTracking(item.Id);

            Status = $"'{item.Name}' is gone for good. Any instance that was on it is no longer tracking a profile.";

            await ReloadAsync();
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.ProfileInUseBySavegame)
        {
            // The one thing that outlives archiving: a save whose mod list is gone is not
            // restorable, which is the only thing that made keeping it worth anything.
            await _modalService.Show(ConfirmationDialogViewModel.Refusal(
                "A savegame still needs it",
                $"'{item.Name}' cannot be deleted while a savegame follows it or was played on one of "
                    + "its revisions. Archive or delete those savegames first."));
        }
        catch (OperationCanceledException)
        {
            // Navigated away.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "deleting an archived profile");
        }
        finally
        {
            IsWorking = false;
        }
    }

    private async Task DeleteSavegameAsync(ArchivedItemViewModel item)
    {
        if (await ConfirmDeleteAsync(item) is false)
        {
            return;
        }

        IsWorking = true;

        try
        {
            await _savegamesClient.DeleteSavegameV1Async(_repo.Id, item.Id, _lifetime.Token);

            Status = $"'{item.Name}' is gone for good.";

            await ReloadAsync();
        }
        catch (OperationCanceledException)
        {
            // Navigated away.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "deleting an archived savegame");
        }
        finally
        {
            IsWorking = false;
        }
    }

    private async Task<bool> ConfirmDeleteAsync(ArchivedItemViewModel item)
    {
        var modal = ConfirmationDialogViewModel.ConfirmDelete(item.Name);

        await _modalService.Show(modal);

        return modal.Result;
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(HasSavegames));
        OnPropertyChanged(nameof(IsEmpty));
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoArchivePageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<RepoArchivePageViewModel>(serviceProvider, repo);
    }
}

