using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Wpf.Shell.BackgroundTasks;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Toasts;

namespace ModsDude.Client.Wpf.Savegames;

/// <remarks>
/// Sibling to <c>ProfileSaveService</c> and shaped like it: it claims the resources, owns the strip
/// entry and its Cancel, and raises its modals through the shell rather than through a page that may
/// already be gone. The modal host is taken lazily because it is the shell itself.
/// </remarks>
public sealed class SavegameOperations(
    IResourceLeases leases,
    ISavegameBindingStore bindings,
    ILocalSavegameAdapters adapters,
    Lazy<IModalService> modalService,
    IErrorReporter errorReporter,
    IBackgroundTaskReporter backgroundTasks,
    IToastService toasts,
    ILogger<SavegameOperations> logger) : ISavegameOperations
{
    public async Task<bool> RunAsync(SavegameOperation operation, Func<IBackgroundTask, CancellationToken, Task> work)
    {
        using var lease = leases.TryAcquireExclusive(KeysOf(operation), operation.Title);

        if (lease is null)
        {
            await modalService.Value.Show(ConfirmationModalViewModel.Refusal(
                "Something else is using that savegame",
                $"{DescribeBusy(operation.Game, operation.Savegames.FirstOrDefault()) ?? "Another savegame task"} is running. Try again once it has finished."));

            return false;
        }

        using var stop = new CancellationTokenSource();
        using var task = backgroundTasks.Begin(operation.Title, operation.Detail, stop.Cancel);

        try
        {
            await work(task, stop.Token);

            return true;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            logger.LogInformation("{Operation} was stopped.", operation.Title);

            toasts.Show(operation.Stopped, ToastSeverity.Warning);

            return false;
        }
        catch (Exception exception)
        {
            await errorReporter.ShowAsync(exception, operation.Doing);

            return false;
        }
    }

    public bool IsBusy(Game game, Guid? savegameId = null)
        => KeysToAsk(game, savegameId).Any(leases.IsHeld);

    public string? DescribeBusy(Game game, Guid? savegameId = null)
        => KeysToAsk(game, savegameId).Select(leases.DescribeHolder).FirstOrDefault(x => x is not null);


    /// <summary>
    /// The savegames named, and the folders: every one of the game's where the operation asks for all
    /// of them, and otherwise the ones the named savegames are held in.
    /// </summary>
    private IEnumerable<string> KeysOf(SavegameOperation operation)
    {
        var folders = operation.AllFolders
            ? FoldersOf(operation.Game)
            : operation.Savegames
                .Select(x => bindings.GetBinding(operation.Game.Identity, x)?.Slot.Target)
                .OfType<TargetKey>();

        return
        [
            .. operation.Savegames.Select(ResourceKeys.Savegame),
            .. folders.Select(x => ResourceKeys.SavegameFolder(operation.Game.Identity, x)).Distinct(StringComparer.Ordinal)
        ];
    }

    private IEnumerable<string> KeysToAsk(Game game, Guid? savegameId)
    {
        var folders = FoldersOf(game).Select(x => ResourceKeys.SavegameFolder(game.Identity, x));

        return savegameId is Guid id ? [ResourceKeys.Savegame(id), .. folders] : folders;
    }

    /// <summary>
    /// Every savegame folder the game reaches now, and every one it still holds a save in - a folder
    /// the settings stopped naming still has a hold in it that a gesture can be about.
    /// </summary>
    private IEnumerable<TargetKey> FoldersOf(Game game)
    {
        IEnumerable<TargetKey> configured = adapters.TryGet(game)?.SavegameTargets.Select(x => x.Key) ?? [];

        return configured
            .Concat(bindings.GetBindings(game.Identity).Select(x => x.Slot.Target))
            .DistinctBy(x => x.Value, StringComparer.OrdinalIgnoreCase);
    }
}
