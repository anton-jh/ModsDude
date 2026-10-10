using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Users;
using ModsDude.Client.Wpf.Account;
using ModsDude.Client.Wpf.Games;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.Repos;

public partial class CreateRepoFormViewModel(
    IRepoStore repoStore,
    IGameAdapterIndex gameAdapterIndex,
    INavigationLockService navigationLockService,
    IFilePickerService filePickerService,
    IModalService modalService,
    ICurrentUserStore currentUser,
    AccountViewModel account,
    TrustCodeFormViewModel trustCode,
    ILogger<CreateRepoFormViewModel> logger)
    : ObservableObject, IDisposable
{
    public AccountViewModel Account { get; } = account;

    public TrustCodeFormViewModel TrustCode { get; } = trustCode;


    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private IGameAdapter? _selectedGameAdapter;

    // Held rather than derived: the editor owns the values the user is typing, so handing out a new
    // one per binding read would submit an empty form.
    [ObservableProperty]
    private DynamicFormViewModel? _baseSettingsEditor;


    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Name) &&
        SelectedGameAdapter is not null &&
        (BaseSettingsEditor?.IsValid ?? false);

    public ObservableCollection<IGameAdapter> AvailableGameAdapters { get; } =
        new(gameAdapterIndex.GetAllLatest());


    [RelayCommand]
    private async Task Submit(CancellationToken cancellationToken)
    {
        if (!IsValid || SelectedGameAdapter is null || string.IsNullOrWhiteSpace(Name) || BaseSettingsEditor is null)
        {
            var modal = ConfirmationModalViewModel.ValidationErrors(GetValidationErrors());
            await modalService.Show(modal);

            return;
        }

        navigationLockService.ReleaseLock(this);

        try
        {
            await repoStore.CreateRepo(
                Name,
                SelectedGameAdapter.Id.ToString(),
                BaseSettingsEditor.ExtractResults(),
                cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.NotTrusted)
        {
            logger.LogInformation(exception, "Creating a repo was refused because the user is not trusted.");

            // Trust was revoked since the user was last read. Reading it again swaps this form for the trust code box.
            await currentUser.RefreshAsync(cancellationToken);
        }
    }

    public void Dispose()
    {
        navigationLockService.ReleaseLock(this);
        BaseSettingsEditor?.Dispose();
    }


    private List<string> GetValidationErrors()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Name is required.");
        }
        if (SelectedGameAdapter is null)
        {
            errors.Add("Game adapter is required.");
        }
        errors.AddRange(BaseSettingsEditor?.GetValidationErrors() ?? []);

        return errors;
    }


    partial void OnNameChanged(string value)
    {
        navigationLockService.AcquireLock(this);
    }

    partial void OnSelectedGameAdapterChanged(IGameAdapter? value)
    {
        BaseSettingsEditor?.Dispose();
        BaseSettingsEditor = value?.GetBaseSettingsTemplate() is DynamicForm template
            ? new DynamicFormViewModel(editing: false, template, filePickerService)
            : null;

        navigationLockService.AcquireLock(this);
    }
}
