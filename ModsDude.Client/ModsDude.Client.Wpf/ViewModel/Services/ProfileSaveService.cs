using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.Concurrent;
using System.Windows;

namespace ModsDude.Client.Wpf.ViewModel.Services;

/// <summary>
/// How a save went.
/// </summary>
public enum ProfileSaveStatus
{
    /// <summary>The revision was written. The apply, where there was one, is reported separately.</summary>
    Saved,

    /// <summary>
    /// Something is already saving this profile. Only reachable as a race - every route in asks the
    /// lease before offering the gesture.
    /// </summary>
    Refused,

    /// <summary>
    /// The import fell short, so nothing was written at all. The draft is left exactly where it was,
    /// with every row that did not make it still pending and marked.
    /// </summary>
    ImportFailed,

    /// <summary>Somebody else's revision was taken instead, so nothing of this one was saved.</summary>
    Superseded,

    /// <summary>Stopped part way. Anything already registered stayed registered.</summary>
    Stopped,

    Failed
}


/// <param name="Saved">What the profile holds now, so a page can move its own baseline onto it.</param>
/// <param name="Changes">The diff the summary is worded from, or null where nothing was written.</param>
public sealed record ProfileSaveOutcome(ProfileSaveStatus Status, string Message)
{
    public int Revision { get; init; }
    public ProfileModListChanges? Changes { get; init; }
    public IReadOnlyList<ProfileModPin> Saved { get; init; } = [];

    /// <summary>
    /// Whether a revision was written. False for a save that only changed what is ignored, which is
    /// not an event a folder can have drifted from.
    /// </summary>
    public bool RevisionWritten { get; init; }

    /// <summary>What the re-apply did, where the save asked for one. Null where it did not.</summary>
    public string? ApplyMessage { get; init; }

    /// <summary>How loudly to say <see cref="ApplyMessage"/>: whether the apply left the folder as it should be.</summary>
    public ToastSeverity ApplySeverity { get; init; } = ToastSeverity.Info;

    public bool Succeeded => Status is ProfileSaveStatus.Saved;
}


/// <summary>
/// Everything a save needs, taken before a byte is uploaded.
/// </summary>
/// <remarks>
/// <b>A save writes what was on screen when Save was pressed.</b> The desired list used to be read
/// out of the live draft <em>after</em> the import, so a row added during an upload was saved without
/// having been imported and a source toggled during one replaced the draft with the server's own
/// list - which the save then wrote back as a revision that changed nothing. A snapshot cannot do
/// either.
/// </remarks>
/// <param name="Pending">The versions the repo does not hold yet, which the import has to register first.</param>
/// <param name="Names">What each pending version is called, for the strip line and the failure dialog.</param>
/// <param name="Catalog">
/// The starting page's catalog, invalidated when the run ends however it ends. Passed rather than
/// owned, exactly as <see cref="ModImportCoordinator"/> takes it.
/// </param>
/// <param name="IgnoredOriginal">The ignore list as the server held it when the draft was read.</param>
/// <param name="IgnoredDesired">
/// What the profile should ignore after the save, with whatever the draft pins already taken out. It is
/// written <em>after</em> the revision and only if that succeeded, and on its own where the mod list did
/// not change - which mints no revision, imports nothing and applies nothing.
/// </param>
/// <param name="Apply">Whether to re-apply afterwards. <em>Save only</em> is what clears it.</param>
public sealed record ProfileSaveRequest(
    Repo Repo,
    Guid ProfileId,
    string ProfileName,
    int BasedOn,
    string? Label,
    IReadOnlyList<ProfileModPin> Original,
    IReadOnlyList<ProfileModPin> Desired,
    IReadOnlyList<ModKey> IgnoredOriginal,
    IReadOnlyList<ModKey> IgnoredDesired,
    IReadOnlyList<CatalogModVersion> Pending,
    IReadOnlyDictionary<ModVersionIdentity, string> Names,
    ModCatalog Catalog,
    bool Apply)
{
    /// <summary>Whether the ignore list differs from what the server holds, and so has to be written.</summary>
    public bool IgnoredChanged => IgnoredDesired.ToHashSet().SetEquals(IgnoredOriginal) is false;
}


/// <summary>
/// One save in flight, and everything a page that arrives part way through needs to draw it.
/// </summary>
/// <remarks>
/// <b>What is retained is the draft, not the page.</b> Keeping the view model alive would need a
/// show/hide lifecycle the page model has never had - the notice suppression, the catalog, the games
/// subscription and the navigation lock are all acquired on construction and released on dispose, and
/// a retained page holds every one of them while somebody is three screens away. A rebuilt page
/// reading this instead gets back the list, its marks and the certainty that nothing was lost. What
/// it does not get back is the scroll position, the selection, the undo bar and the version
/// description, which is the price.
/// </remarks>
public sealed class ProfileSaveRun
{
    private readonly ConcurrentDictionary<ModVersionIdentity, ModImportProgress> _progress = new();
    private readonly ConcurrentDictionary<ModVersionIdentity, ModImportItemResult> _results = new();


    internal ProfileSaveRun(ProfileSaveRequest request)
    {
        Request = request;
        Completion = Task.FromResult(new ProfileSaveOutcome(ProfileSaveStatus.Failed, "Nothing ran."));
    }


    /// <summary>
    /// Raised whenever a version moved, from whatever thread the import is on. A page marshals for
    /// itself; doing it here would tie the run's progress to a dispatcher being alive.
    /// </summary>
    public event Action? Advanced;


    public ProfileSaveRequest Request { get; }

    public Task<ProfileSaveOutcome> Completion { get; internal set; }

    /// <summary>How to stop it. The same act as the strip's Cancel, because it is the same source.</summary>
    public Action? Cancel { get; internal set; }

    /// <summary>The strip entry the whole gesture is drawn on, for the steps that have none of their own.</summary>
    internal IBackgroundTask? Strip { get; set; }

    /// <summary>
    /// Everything the run has reported so far, so a page built half way through starts where the run
    /// is rather than at the beginning.
    /// </summary>
    public IReadOnlyList<ModImportProgress> Progress => [.. _progress.Values];

    /// <inheritdoc cref="Progress"/>
    public IReadOnlyList<ModImportItemResult> Results => [.. _results.Values];


    internal void Report(ModImportProgress value)
    {
        _progress[value.Identity] = value;

        Advanced?.Invoke();
    }

    internal void Report(ModImportItemResult result)
    {
        _results[result.Identity] = result;

        Advanced?.Invoke();
    }
}


/// <summary>
/// A save is a gesture, not a page: import, revision, re-apply and drift check, claimed on the
/// profile and reported on the shell strip.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sibling to <see cref="ProfileApplyService"/> and <see cref="ModImportCoordinator"/>, and shaped
/// like them</b> - it claims the resource, owns the strip entry and its Cancel, and raises its
/// dialogs through the shell's modal host rather than through a page that may already be gone.
/// Before this, navigating away disposed the editor and its catalog mid-flight: the files finished
/// registering on the background strip, the revision was never written, and nothing said so.
/// </para>
/// <para>
/// <b>The claim is on the profile, and the refusal is exactly as wide as the lease.</b> A save writes
/// a revision of one profile and imports into one repo, and the two are different resources - so a
/// second profile in the same repo whose draft has nothing to import is a revision write and saves
/// perfectly well beside an import. Only a draft with mods to import has to wait, and the page it
/// belongs to says what for. Being unable to write for a few minutes is not a reason to be unable to
/// think for a few minutes.
/// </para>
/// <para>
/// <b>It reports per version rather than into rows it does not own.</b> A page subscribes to
/// <see cref="ProfileSaveRun"/> and marks its own rows, which is what lets an editor rebuilt half way
/// through a save show the marks the run has already made.
/// </para>
/// <para>
/// The modal host is taken lazily for the reason it is everywhere else: it is the shell, and the
/// shell builds the things that build this.
/// </para>
/// </remarks>
public sealed class ProfileSaveService(
    ModImportCoordinator imports,
    ProfileApplyService applyService,
    IProfilesClient profilesClient,
    IModDependenciesClient dependenciesClient,
    GameRepository games,
    DriftMonitor driftMonitor,
    Lazy<IModalService> modalService,
    IErrorReporter errorReporter,
    IBackgroundTaskReporter backgroundTasks,
    IToastService toasts,
    IResourceLeases leases)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, ProfileSaveRun> _runs = [];


    /// <summary>
    /// The save running on this profile, or null. <b>What a rebuilt editor asks before it asks the
    /// server:</b> a page that read the profile fresh while a save was uploading would draw the list
    /// the save is about to replace.
    /// </summary>
    public ProfileSaveRun? Find(Guid profileId)
    {
        lock (_gate)
        {
            return _runs.GetValueOrDefault(profileId);
        }
    }

    /// <summary>
    /// Whether a save of this profile would be refused right now. A hint for a <c>CanExecute</c>, and
    /// not the guard - <see cref="RunAsync"/> is what actually decides.
    /// </summary>
    public bool IsSaving(Guid profileId) => leases.IsHeld(ResourceKeys.Profile(profileId));

    /// <summary>
    /// Whether an import into this repo would be refused right now, and by what. Read by an editor of
    /// a <em>different</em> profile, whose Save is only blocked if it has mods to import.
    /// </summary>
    public string? DescribeImportBusy(Guid repoId) => imports.DescribeBusy(repoId);

    /// <summary>
    /// Imports whatever the draft pins and the repo does not hold, writes the revision, re-applies,
    /// and re-checks drift - in that order, because a mod is never registered before its file is in
    /// storage and a dependency can only name a registered version.
    /// </summary>
    /// <remarks>
    /// <b>An import that does not fully succeed stops the save.</b> The steps after it are written
    /// against the mods the repo now holds, so carrying on with a short list quietly turns "these
    /// files failed to upload" into a profile that never mentions them and an apply that treats them
    /// as unrecognised - which sends the very files the user was importing to the Recycle Bin, one
    /// confirmation click away. Nothing downstream can tell that apart from a folder full of junk,
    /// so the only place it can be caught is here, before anything is written.
    /// <para>
    /// <b>How it went is said here, as toasts</b>, whether or not an editor is on screen to draw it:
    /// the editor may have been navigated away from, and a save is a gesture rather than a page. The
    /// editor reports nothing of its own about the outcome - it only marks its rows.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The run, so the caller can mark its own rows. A refusal comes back as a run that has already
    /// finished, so there is one shape for a caller to handle rather than two.
    /// </returns>
    public ProfileSaveRun Start(ProfileSaveRequest request)
    {
        var run = new ProfileSaveRun(request);

        var lease = leases.TryAcquireExclusive(
            ResourceKeys.Profile(request.ProfileId),
            $"Saving '{request.ProfileName}'");

        if (lease is null)
        {
            run.Completion = Task.FromResult(new ProfileSaveOutcome(
                ProfileSaveStatus.Refused,
                $"'{request.ProfileName}' is already being saved. Nothing was written."));

            Report(run.Completion.Result);

            return run;
        }

        lock (_gate)
        {
            _runs[request.ProfileId] = run;
        }

        run.Completion = FinishAsync(run, lease);

        return run;
    }


    private async Task<ProfileSaveOutcome> FinishAsync(ProfileSaveRun run, IResourceLease lease)
    {
        var request = run.Request;

        using var _ = lease;
        using var stop = new CancellationTokenSource();

        run.Cancel = stop.Cancel;

        // The gesture's own entry, above the import's and the apply's. It is what makes a save
        // visible - and stoppable - once the page that started it has been navigated away from,
        // which is the half that was missing: the strip outlives the page and the page does not.
        using var task = backgroundTasks.Begin($"Saving '{request.ProfileName}'", cancel: stop.Cancel);

        run.Strip = task;

        try
        {
            var outcome = await SaveAsync(run, stop.Token);

            // Unconditionally, and whatever the save did. A save mints a revision, and every folder
            // built against the previous one is drifted from that moment - including where the save
            // did not apply, which is precisely the case where the drift is real and nothing else
            // would have looked.
            if (outcome.Succeeded && outcome.RevisionWritten)
            {
                await driftMonitor.CheckAsync();
            }

            return Retire(run, outcome);
        }
        catch (OperationCanceledException)
        {
            return Retire(run, new ProfileSaveOutcome(
                ProfileSaveStatus.Stopped,
                "Save stopped. Anything already registered stayed registered."));
        }
        catch (Exception exception)
        {
            return Retire(run, new ProfileSaveOutcome(
                ProfileSaveStatus.Failed,
                await DescribeFailureAsync(exception, request)));
        }
    }

    private async Task<ProfileSaveOutcome> SaveAsync(ProfileSaveRun run, CancellationToken cancellationToken)
    {
        var request = run.Request;

        if (ProfileModListDiff.Compute(request.Original, request.Desired).IsEmpty)
        {
            return await SaveIgnoredOnlyAsync(run, cancellationToken);
        }

        run.Strip?.Report(request.Pending.Count == 0
            ? "Writing the revision"
            : $"Importing {request.Pending.Count} mods");

        var import = await ImportAsync(run, cancellationToken);

        if (import.Refusal is string refusal)
        {
            // Nothing registered, so nothing is pinnable and the save must not proceed on a draft
            // whose mods are still only on disk. The draft is left exactly where it is, so pressing
            // Save again once the other import finishes is the whole recovery path.
            return new ProfileSaveOutcome(ProfileSaveStatus.ImportFailed, refusal);
        }

        var unfinished = request.Pending.Count(x => import.Imported.Contains(x.Identity) is false);

        if (unfinished > 0)
        {
            if (import.Problems is not null)
            {
                await OnUiThreadAsync(() => modalService.Value.Show(import.Problems));
            }

            return new ProfileSaveOutcome(
                ProfileSaveStatus.ImportFailed,
                $"{unfinished} could not be imported, so nothing was saved.");
        }

        run.Strip?.Report("Writing the revision");

        var written = await WriteRevisionAsync(request, cancellationToken);

        if (written is not int revision)
        {
            return new ProfileSaveOutcome(
                ProfileSaveStatus.Superseded,
                "Loaded the newer list. Nothing of yours was saved.");
        }

        // The revision is written, so the import that fed it is finally paid for and the copies the
        // user chose against can go. Not one line earlier: everything above this can still return
        // without saving, and a file removed for a revision that never existed is gone for nothing.
        imports.RecycleSuperseded(import.Superseded);

        var changes = ProfileModListDiff.Compute(request.Original, request.Desired);

        // Only now, and only because the revision is written: the ignore list is not atomic with it,
        // and a list saved beside a mod list that was not would be half a save. A failure here does
        // not undo the revision - it is said, beside what did save.
        var ignoredNote = await WriteIgnoredAsync(run, cancellationToken);

        var applied = await ApplyAsync(request, cancellationToken);

        return new ProfileSaveOutcome(
            ProfileSaveStatus.Saved,
            ignoredNote is null ? Describe(changes) : $"{Describe(changes)} {ignoredNote}")
        {
            Revision = revision,
            Changes = changes,
            Saved = request.Desired,
            RevisionWritten = true,
            ApplyMessage = applied?.Message,
            ApplySeverity = applied?.Severity ?? ToastSeverity.Info
        };
    }

    /// <summary>
    /// A save whose mod list did not change: the ignore list, and nothing else. No revision, no import,
    /// no apply and no drift check - nothing a folder was built from has moved, so there is nothing to
    /// bring back into line.
    /// </summary>
    private async Task<ProfileSaveOutcome> SaveIgnoredOnlyAsync(ProfileSaveRun run, CancellationToken cancellationToken)
    {
        var request = run.Request;

        var failure = await WriteIgnoredAsync(run, cancellationToken);

        if (failure is not null)
        {
            return new ProfileSaveOutcome(ProfileSaveStatus.Failed, failure);
        }

        var count = request.IgnoredDesired.Count;

        return new ProfileSaveOutcome(
            ProfileSaveStatus.Saved,
            count switch
            {
                0 => "Saved. Nothing is ignored in this profile now.",
                1 => "Saved. 1 mod is ignored in this profile.",
                _ => $"Saved. {count} mods are ignored in this profile."
            })
        {
            // Unchanged: no revision was written, and the page's baseline must stay on the one it has.
            Revision = request.BasedOn,
            Saved = request.Desired
        };
    }

    /// <summary>
    /// Replaces what the profile ignores with the draft's list, when it differs. Null on success, and
    /// otherwise the sentence saying it did not.
    /// </summary>
    /// <remarks>
    /// The whole list rather than a change to it: the list belongs to one profile and is saved the way
    /// the mod list is, as what the page showed. See <c>ProfileIgnoredMod</c>.
    /// </remarks>
    private async Task<string?> WriteIgnoredAsync(ProfileSaveRun run, CancellationToken cancellationToken)
    {
        var request = run.Request;

        if (request.IgnoredChanged is false)
        {
            return null;
        }

        run.Strip?.Report("Saving the ignored mods");

        try
        {
            await profilesClient.SetProfileIgnoredModsV1Async(
                request.Repo.Id,
                request.ProfileId,
                new SetProfileIgnoredModsRequest { ModIds = [.. request.IgnoredDesired.Select(x => x.Value)] },
                cancellationToken);

            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await OnUiThreadAsync(() => errorReporter.ShowAsync(exception, $"saving the ignored mods of '{request.ProfileName}'"));

            return "The ignored mods could not be saved, so they are as they were.";
        }
    }

    /// <summary>
    /// Uploads and registers the rows that are still only on disk, and reports which of them the repo
    /// now holds - and, where some did not make it, the one dialog that says so.
    /// </summary>
    private async Task<PendingImport> ImportAsync(ProfileSaveRun run, CancellationToken cancellationToken)
    {
        var request = run.Request;

        if (request.Pending.Count == 0)
        {
            return new PendingImport([], null, [], null);
        }

        var outcome = await imports.RunAsync(
            request.Repo,
            request.Pending,
            request.Names,
            request.Catalog,
            new ProgressRelay(run),
            cancellationToken);

        if (outcome is { Refusal: string refusal })
        {
            return new PendingImport([], null, [], refusal);
        }

        var result = outcome.Result ?? ModImportResult.Empty;

        foreach (var item in result.Items)
        {
            run.Report(item);
        }

        var problems = await OnUiThreadAsync(() => Task.FromResult(ModImportProblems.Build(
            errorReporter,
            result,
            id => request.Names.GetValueOrDefault(id, id.ModId.Value),
            "Nothing was saved.")));

        return new PendingImport([.. result.Succeeded.Select(x => x.Identity)], problems, result.Superseded, null);
    }

    /// <summary>
    /// Writes the whole mod list as a new revision, and returns the number it became. Null means the
    /// user chose somebody else's newer list over their own, so nothing was written.
    /// </summary>
    /// <remarks>
    /// One request, carrying every pin. This used to be a delete, an upgrade batch and an add or
    /// update per changed mod; what goes over the wire now is the list itself, because a revision is a
    /// snapshot and the server has to record exactly what the page showed.
    /// </remarks>
    private async Task<int?> WriteRevisionAsync(ProfileSaveRequest request, CancellationToken cancellationToken)
    {
        var basedOn = request.BasedOn;

        while (true)
        {
            var body = new SaveProfileRevisionRequest
            {
                BasedOn = basedOn,
                Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim(),
                Mods = [.. request.Desired.Select(x => new ProfileModPinRequest
                {
                    ModId = x.ModId.Value,
                    VersionId = x.VersionId.Value,
                    Locked = x.Lock.ByProfile
                })]
            };

            try
            {
                var saved = await profilesClient.SaveProfileRevisionV1Async(
                    request.Repo.Id, request.ProfileId, body, cancellationToken);

                return saved.Number;
            }
            catch (ApiException<CustomProblemDetails> exception)
                when (exception.Result.Type is ProblemType.ProfileRevisionStale)
            {
                if (await ConfirmStaleSaveAsync() is false)
                {
                    return null;
                }

                // Re-read only the number, so the retry is based on what the server is actually on
                // rather than on what the refusal happened to mention.
                var current = await dependenciesClient.GetModDependenciesV1Async(
                    request.Repo.Id, request.ProfileId, null, cancellationToken);

                basedOn = current.Revision;
            }
        }
    }

    /// <summary>
    /// Somebody else saved this profile while this list was open. The choice is theirs, and both
    /// answers are safe: what is on the server is a revision either way, so saving over it does not
    /// destroy it - it can be restored from the history.
    /// </summary>
    private Task<bool> ConfirmStaleSaveAsync()
    {
        return OnUiThreadAsync(async () =>
        {
            var confirmation = new ConfirmationDialogViewModel(
                "Somebody else saved this profile",
                "Your list was built from an older revision. Saving anyway records yours as the newest one - theirs stays in the history and can be restored. Loading theirs discards what you have here.",
                IconKind.Warning,
                "Save mine anyway",
                "Load theirs");

            await modalService.Value.Show(confirmation);

            return confirmation.Result;
        });
    }

    /// <summary>
    /// Re-applies to the game that follows this profile, where one does.
    /// </summary>
    /// <remarks>
    /// <b>Pure apply, never an activation.</b> The profile is already what that game follows, so
    /// there is no intent to record - saving a mod list is not a decision about which list a game is
    /// on. A folder that cannot be applied to right now is reported and left drifted, which the
    /// app-level notice already covers. The onboarding offer for a profile nothing is using stays on
    /// the page: it is a mode change with a chosen target, which is a different operation and needs
    /// somebody looking at it.
    /// </remarks>
    private async Task<ApplyReport?> ApplyAsync(ProfileSaveRequest request, CancellationToken cancellationToken)
    {
        if (request.Apply is false)
        {
            return new ApplyReport("Saved without applying. Your installed mods are untouched.", ToastSeverity.Info);
        }

        if (games.GetGameFollowing(request.Repo.Scope, new ActiveProfile(request.Repo.Id, request.ProfileId))
            is not Game game)
        {
            return null;
        }

        var outcome = await applyService.ApplyAsync(
            request.Repo,
            game,
            request.ProfileId,
            request.ProfileName,
            confirmPlan: false,
            progress: null,
            cancellationToken);

        return new ApplyReport(outcome.Message, outcome.ToastSeverity);
    }

    private sealed record ApplyReport(string Message, ToastSeverity Severity);

    private async Task<string> DescribeFailureAsync(Exception exception, ProfileSaveRequest request)
    {
        await OnUiThreadAsync(() => errorReporter.ShowAsync(exception, $"saving '{request.ProfileName}'"));

        return "The save did not finish. Nothing about the list was lost; try again.";
    }

    /// <summary>
    /// Takes the run off the register and says how it went.
    /// </summary>
    /// <remarks>
    /// Said the same whether or not anybody is looking at the editor, which is what "a save that
    /// finished while you were elsewhere says so" comes to now. It used to be filed for the notice
    /// column when nobody was watching and left to the page when somebody was, which was two routes
    /// to one sentence and a race between them.
    /// </remarks>
    private ProfileSaveOutcome Retire(ProfileSaveRun run, ProfileSaveOutcome outcome)
    {
        lock (_gate)
        {
            _runs.Remove(run.Request.ProfileId);
        }

        Report(outcome);

        return outcome;
    }

    /// <summary>
    /// The sentence, and beside it - where the save went on to apply - the sentence about that.
    /// </summary>
    /// <remarks>
    /// A failure was already put in front of the user as a dialog where it had anything to add, so
    /// what is said here is the short version of what was left undone: the draft is still open and
    /// saving again is the way on.
    /// </remarks>
    private void Report(ProfileSaveOutcome outcome)
    {
        var severity = outcome.Status is ProfileSaveStatus.Saved or ProfileSaveStatus.Stopped
            ? ToastSeverity.Info
            : ToastSeverity.Warning;

        toasts.Show(outcome.Message, severity);

        if (outcome.ApplyMessage is string applied)
        {
            toasts.Show(applied, outcome.ApplySeverity);
        }
    }

    private static string Describe(ProfileModListChanges changes)
    {
        if (changes.IsEmpty)
        {
            return "Nothing to save.";
        }

        var parts = new List<string>();

        if (changes.Added.Count > 0)
        {
            parts.Add($"{changes.Added.Count} added");
        }

        if (changes.Changed.Count > 0)
        {
            parts.Add($"{changes.Changed.Count} changed");
        }

        if (changes.Removed.Count > 0)
        {
            parts.Add($"{changes.Removed.Count} removed");
        }

        return string.Join(" · ", parts);
    }

    /// <inheritdoc cref="ModImportCoordinator" path="/remarks"/>
    private static Task<T> OnUiThreadAsync<T>(Func<Task<T>> work)
        => Application.Current.Dispatcher.InvokeAsync(work).Task.Unwrap();

    private static Task OnUiThreadAsync(Func<Task> work)
        => Application.Current.Dispatcher.InvokeAsync(work).Task.Unwrap();


    /// <param name="Imported">What the repo holds now, which is what the save is allowed to pin.</param>
    /// <param name="Problems">The dialog for what did not make it, or null when everything did.</param>
    /// <param name="Superseded">
    /// Copies the user chose against where two sources disagreed. Held until the save commits: here
    /// the import is only the first half of the action, and a file removed for a revision that was
    /// never written is a file removed for nothing.
    /// </param>
    /// <param name="Refusal">Why the import never started, in a sentence. Null whenever it did.</param>
    private sealed record PendingImport(
        HashSet<ModVersionIdentity> Imported,
        ErrorDialogViewModel? Problems,
        IReadOnlyList<ModSupersededFile> Superseded,
        string? Refusal);

    /// <summary>
    /// Turns the import's reports into the run's, so whatever is drawing the run marks its own rows.
    /// </summary>
    private sealed class ProgressRelay(ProfileSaveRun run) : IProgress<ModImportProgress>
    {
        public void Report(ModImportProgress value) => run.Report(value);
    }
}
