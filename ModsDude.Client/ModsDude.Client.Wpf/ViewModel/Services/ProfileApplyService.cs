using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.IO;

namespace ModsDude.Client.Wpf.ViewModel.Services;

public enum ProfileApplyStatus
{
    /// <summary>The folder now matches the profile.</summary>
    Applied,

    /// <summary>It already did, so nothing was touched.</summary>
    AlreadyMatched,

    /// <summary>The user backed out of the confirmation.</summary>
    Declined,

    /// <summary>
    /// A dedicated server mid-session, a folder held by a running game, an unplugged drive. Reported
    /// and left drifted - which is a "not now", and which the drift notice already covers.
    /// </summary>
    Unavailable,

    Failed
}

public sealed record ProfileApplyOutcome(LocalInstance Instance, ProfileApplyStatus Status, string Message)
{
    public bool Succeeded => Status is ProfileApplyStatus.Applied or ProfileApplyStatus.AlreadyMatched;
}


/// <summary>
/// Applying a profile to an instance from anywhere that is not the sync page: the drift notice's
/// one-click re-apply, the mod list editor's save, and the shell-level activation control.
/// </summary>
/// <remarks>
/// The sync page stays as it is - it exists to show the plan and let the user read it before
/// deciding. This is the other shape, where the decision has already been made and the plan is only
/// worth interrupting for when it would destroy something the repo cannot put back.
/// </remarks>
public sealed class ProfileApplyService(ModSyncService syncService, IModalService modalService)
{
    /// <summary>
    /// Works out what would change. Returns null where the instance cannot be applied to right now,
    /// with the reason in <paramref name="unavailable"/>.
    /// </summary>
    public async Task<ModSyncPlan?> TryPlanAsync(
        Repo repo,
        LocalInstance instance,
        Guid profileId,
        string? profileName,
        CancellationToken cancellationToken)
    {
        if (GetAdapter(repo, instance) is not IInstanceModAdapter adapter)
        {
            return null;
        }

        try
        {
            return await syncService.PlanAsync(
                new ModSyncRequest(instance.Id, adapter, repo.Id, profileId) { ProfileName = profileName },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is UserFriendlyException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Plans, confirms anything unrecoverable, and executes. One call, because the caller has already
    /// decided - the whole point of the drift notice's second action is that it costs one click.
    /// </summary>
    /// <param name="confirmPlan">
    /// Whether to show the plan before executing. Activation moves an instance onto a different
    /// profile, which uninstalls whatever the previous one put there; the reconciler already knows
    /// exactly what that is, so it is shown rather than a bare "are you sure". A re-apply of the
    /// profile the instance is already on has nothing to disclose beyond the destructive part, which
    /// is confirmed either way.
    /// </param>
    public async Task<ProfileApplyOutcome> ApplyAsync(
        Repo repo,
        LocalInstance instance,
        Guid profileId,
        string? profileName,
        bool confirmPlan,
        IProgress<ModSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        ModSyncPlan? plan;

        try
        {
            plan = await TryPlanAsync(repo, instance, profileId, profileName, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ProfileApplyOutcome(instance, ProfileApplyStatus.Declined, $"'{instance.Name}' was stopped before anything changed.");
        }

        if (plan is null)
        {
            return new ProfileApplyOutcome(
                instance,
                ProfileApplyStatus.Unavailable,
                $"'{instance.Name}' could not be reached, so it was left as it is. It will keep showing as drifted until it can be.");
        }

        if (plan.HasWork is false)
        {
            return new ProfileApplyOutcome(instance, ProfileApplyStatus.AlreadyMatched, $"'{instance.Name}' already matches.");
        }

        if (confirmPlan && await ConfirmPlanAsync(instance, plan) is false)
        {
            return new ProfileApplyOutcome(instance, ProfileApplyStatus.Declined, $"'{instance.Name}' was left as it is.");
        }

        if (plan.Unrecognised.Count > 0 && await ConfirmUnrecognisedAsync(plan) is false)
        {
            return new ProfileApplyOutcome(instance, ProfileApplyStatus.Declined, $"'{instance.Name}' was left as it is.");
        }

        try
        {
            var result = await syncService.ExecuteAsync(plan, progress, cancellationToken);

            return result.Completed
                ? new ProfileApplyOutcome(instance, ProfileApplyStatus.Applied, $"'{instance.Name}' now matches.")
                : new ProfileApplyOutcome(
                    instance,
                    ProfileApplyStatus.Failed,
                    $"'{instance.Name}': {result.Failures.Count} mods could not be applied.");
        }
        catch (OperationCanceledException)
        {
            return new ProfileApplyOutcome(instance, ProfileApplyStatus.Declined, $"'{instance.Name}' was stopped part way.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ProfileApplyOutcome(
                instance,
                ProfileApplyStatus.Unavailable,
                $"'{instance.Name}' is in use - a running game or a server mid-session holds its folder. It was left drifted.");
        }
    }

    /// <summary>
    /// The reconciler's own plan as the confirmation. It already computes exactly what would change,
    /// so showing it beats asking "are you sure" about something the user cannot see.
    /// </summary>
    public async Task<bool> ConfirmPlanAsync(LocalInstance instance, ModSyncPlan plan)
    {
        var lines = new List<string>();

        if (plan.InstallCount > 0) lines.Add($"{plan.InstallCount} to install");
        if (plan.ReplaceCount > 0) lines.Add($"{plan.ReplaceCount} to replace");
        if (plan.UninstallCount > 0) lines.Add($"{plan.UninstallCount} to uninstall");
        if (plan.QuarantineCount > 0) lines.Add($"{plan.QuarantineCount} to move to the Recycle Bin");

        var modal = new ConfirmationDialogViewModel(
            $"Apply to '{instance.Name}'?",
            $"{plan.ModFolder}\n\n" +
            $"{string.Join('\n', lines)}\n" +
            $"{plan.KeepCount} already correct.\n\n" +
            "Anything the profile does not pin is taken out of the folder.",
            IconKind.Question,
            "Apply",
            "Cancel");

        await modalService.Show(modal);

        return modal.Result;
    }

    /// <summary>
    /// The one interruption a re-apply is always worth: files nothing else on the machine has a copy
    /// of, named, with where they are going.
    /// </summary>
    public async Task<bool> ConfirmUnrecognisedAsync(ModSyncPlan plan)
    {
        var names = plan.Unrecognised.Take(10).Select(x => $"  {x.DisplayName}");
        var more = plan.Unrecognised.Count > 10 ? $"\n  ...and {plan.Unrecognised.Count - 10} more" : "";

        var modal = new ConfirmationDialogViewModel(
            "These are not in the repo",
            $"{plan.Unrecognised.Count} installed files are not registered in this repo, so nothing else has a copy of them:\n\n" +
            $"{string.Join('\n', names)}{more}\n\n" +
            "They will be moved to the Windows Recycle Bin, where you can restore them. Nothing is deleted.",
            IconKind.Warning,
            "Apply the profile",
            "Cancel");

        await modalService.Show(modal);

        return modal.Result;
    }


    private static IInstanceModAdapter? GetAdapter(Repo repo, LocalInstance instance)
    {
        return instance.GetAdapter(repo.Adapter)
            .GetInstanceCapabilityAdapterFactory<IInstanceModAdapter>()
            ?.Invoke();
    }
}
