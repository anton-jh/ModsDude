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


    protected override async Task InitAsync() => await ReloadAsync();

    [RelayCommand]
    private async Task Refresh() => await ReloadAsync();


    private async Task ReloadAsync()
    {
        IsLoading = true;

        try
        {
            var archived = await _repoRepository.GetArchivedRepos(_lifetime.Token);

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
        var confirmation = ConfirmationDialogViewModel.ConfirmDelete(item.Name);

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
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.RepoNotEmpty)
        {
            // The mod versions hold it: every one is a blob somebody could still be syncing, and a
            // repo row deleted out from under them would strand the lot.
            await _modalService.Show(ConfirmationDialogViewModel.Refusal(
                "It still holds mods",
                $"'{item.Name}' cannot be deleted while it has registered mods. Delete those from its "
                    + "Mods page first - it is still reachable while it is archived."));
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
