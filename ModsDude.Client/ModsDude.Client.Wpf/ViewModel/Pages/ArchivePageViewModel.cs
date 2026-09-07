using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.ModsDudeServer.Generated;
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

            Repos.Clear();

            foreach (var membership in archived)
            {
                Repos.Add(new ArchivedItemViewModel(
                    membership.Repo.Id,
                    membership.Repo.Name,
                    membership.Repo.ArchivedAt,
                    membership.MembershipLevel >= RepoMembershipLevel.Admin,
                    RestoreAsync,
                    DeleteAsync));
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
    /// Restores, asking for another name where this one has been taken since. Repo names are unique
    /// across the whole server, so the clash can come from somebody the caller has never met.
    /// </summary>
    private async Task RestoreAsync(ArchivedItemViewModel item)
    {
        IsWorking = true;
        Status = null;

        try
        {
            try
            {
                await _repoRepository.RestoreRepo(item.Id, null, _lifetime.Token);

                Status = $"'{item.Name}' is back in your repos.";
            }
            catch (Core.Exceptions.UserFriendlyException exception) when (exception.Message == "Name taken")
            {
                var modal = new RenameModalViewModel(
                    "That name is taken",
                    $"Another repo is called '{item.Name}' now - repo names are unique across the whole "
                        + "server, so it may not even be one of yours. Give this one another name to bring it back.",
                    $"{item.Name} (restored)");

                await _modalService.Show(modal);

                if (modal.Result is not string renamed)
                {
                    return;
                }

                await _repoRepository.RestoreRepo(item.Id, renamed, _lifetime.Token);

                Status = $"'{renamed}' is back in your repos.";
            }

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
