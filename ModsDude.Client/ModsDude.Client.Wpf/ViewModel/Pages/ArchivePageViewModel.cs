using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// The repos this user is in that have been archived - the top-level Archive.
/// </summary>
/// <remarks>
/// <para>
/// <b>A repo is archived for everybody at once.</b> Archiving is repo state rather than membership
/// state, so this is not a personal "hidden" list: what is here is what the group has put away, and
/// every member sees the same thing.
/// </para>
/// <para>
/// Restoring and deleting are Admin, and a member of the repo who is not one still sees the list -
/// a repo that vanished from the sidebar has to be explainable to whoever noticed.
/// </para>
/// </remarks>
public partial class ArchivePageViewModel : PageViewModel
{
    private readonly RepoRepository _repoRepository;
    private readonly IModalService _modalService;
    private readonly IErrorReporter _errorReporter;

    private readonly CancellationTokenSource _lifetime = new();

    private IReadOnlyList<RepoMembershipDto> _fetched = [];


    public ArchivePageViewModel(
        RepoRepository repoRepository,
        IModalService modalService,
        IErrorReporter errorReporter)
    {
        _repoRepository = repoRepository;
        _modalService = modalService;
        _errorReporter = errorReporter;
    }


    public ObservableCollection<ArchivedItemViewModel> Repos { get; } = [];

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isWorking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;


    public bool HasStatus => Status is not null;

    public bool IsEmpty => IsLoading is false && Repos.Count == 0;


    /// <summary>
    /// The first load runs off the UI thread, so it only fetches. The rows are filled in
    /// <see cref="OnInitCompleted"/>: the list is bound, and a bound collection refuses to be
    /// changed from any thread but the dispatcher's.
    /// </summary>
    protected override async Task InitAsync()
    {
        _fetched = await _repoRepository.GetArchivedRepos(_lifetime.Token);
    }

    protected override void OnInitCompleted()
    {
        Publish(_fetched);

        IsLoading = false;
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// Reported here rather than rethrown, so the modal names what was being read - and so a page
    /// that failed to load stops claiming it is still loading.
    /// </summary>
    protected override void OnInitFailed(Exception exception)
    {
        IsLoading = false;
        OnPropertyChanged(nameof(IsEmpty));

        if (exception is OperationCanceledException)
        {
            // Navigated away.
            return;
        }

        _ = _errorReporter.ShowAsync(exception, "reading the archive");
    }

    [RelayCommand]
    private async Task Refresh() => await ReloadAsync();


    /// <summary>Fills the list. Dispatcher thread only.</summary>
    private void Publish(IReadOnlyList<RepoMembershipDto> archived)
    {
        // The archive is where two repos of a name are most likely to meet: it collects every
        // one this user has put away, from every group they are in.
        var ambiguous = RepoDisplay.FindAmbiguous(archived.Select(x => (x.Repo.Id, x.Repo.Name)));

        Repos.Clear();

        foreach (var membership in archived)
        {
            Repos.Add(new ArchivedItemViewModel(
                membership.Repo.Id,
                membership.Repo.Name,
                membership.Repo.ArchivedAt,
                // Both Admin for a repo: it is the one archived thing whose restore puts it back
                // into every member's sidebar.
                membership.MembershipLevel >= RepoMembershipLevel.Admin,
                membership.MembershipLevel >= RepoMembershipLevel.Admin,
                RestoreAsync,
                DeleteAsync)
            {
                Tag = ambiguous.Contains(membership.Repo.Id) ? membership.Repo.Tag : null
            });
        }
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
            Publish(await _repoRepository.GetArchivedRepos(_lifetime.Token));
        }
        catch (OperationCanceledException)
        {
            // Navigated away.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "reading the archive");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>
    /// Restores, under the repo's own name. Unlike the profile and savegame restores on the repo's
    /// own Archive page there is no rename to ask for: repo names are not unique, so nothing can
    /// have taken this one while it was away.
    /// </summary>
    private async Task RestoreAsync(ArchivedItemViewModel item)
    {
        IsWorking = true;
        Status = null;

        try
        {
            await _repoRepository.RestoreRepo(item.Id, _lifetime.Token);

            Status = $"'{item.Name}' is back in your repos.";

            await ReloadAsync();
        }
        catch (OperationCanceledException)
        {
            // Navigated away.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "restoring an archived repo");
        }
        finally
        {
            IsWorking = false;
        }
    }

    private async Task DeleteAsync(ArchivedItemViewModel item)
    {
        var confirmation = ConfirmationDialogViewModel.ConfirmDeleteRepo(item.Name);

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            return;
        }

        IsWorking = true;

        try
        {
            await _repoRepository.DeleteRepo(item.Id, _lifetime.Token);

            Status = $"'{item.Name}' is gone for good.";

            await ReloadAsync();
        }
        catch (OperationCanceledException)
        {
            // Navigated away.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "deleting an archived repo");
        }
        finally
        {
            IsWorking = false;
        }
    }
}
