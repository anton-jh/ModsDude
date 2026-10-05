using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameFiles;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Sync;

/// <summary>Works out what an apply would do to one mod folder, without changing anything.</summary>
internal sealed class ModSyncPlanBuilder(
    IModDependenciesClient modDependenciesClient,
    IModsClient modsClient,
    IContentStoreProvider storeProvider,
    ISyncManifestStore manifestStore,
    IHeldSavegames heldSavegames,
    IGameFileEditor fileEditor,
    IGameRunningGuard runningGuard,
    ILogger logger)
{
    /// <summary>Matches the import's, since the repo is expected to hold thousands of versions.</summary>
    private const int _registeredPageSize = 500;


    /// <param name="progress">
    /// Where to report which mod is being examined. Optional, and worth passing: on a folder whose
    /// files no longer match the manifest this reads and hashes every one of them, which is the
    /// slowest thing an apply does.
    /// </param>
    public async Task<ModSyncPlan> PlanAsync(
        ModSyncRequest request,
        CancellationToken cancellationToken,
        IProgress<ModSyncProgress>? progress = null)
    {
        var target = request.Target;
        var modFolder = target.Path;

        runningGuard.EnsureNotRunning(request.Game, request.GameName);

        if (Directory.Exists(modFolder) is false)
        {
            throw new UserFriendlyException(
                "That mod folder is not reachable",
                $"'{modFolder}' does not exist right now. An unplugged drive or an offline network path looks like this; nothing has been changed.");
        }

        // Before anything is read, so a store the apply could never write to is the reason given
        // rather than a failure per mod once the folder is half changed.
        storeProvider.GetStoreServing(modFolder).EnsureUsable();

        IReadOnlyList<DesiredMod> desired = [];
        int? revision = null;
        Task<(IReadOnlyList<DesiredMod> Mods, int Revision)>? desiredLoad = null;

        if (request.ClearAll)
        {
            RefuseClearWhileHeld(request);
        }
        else
        {
            desiredLoad = GetDesiredAsync(request, ResolveTargetRevision(request), cancellationToken);
        }

        var manifest = manifestStore.TryRead(request.TargetRef);

        // Started now, beside everything else, where the folder is about to change profile: then it is
        // leaving one list for another and something is almost certainly removed, which is what the
        // repo's mod list is needed for - and whatever it is taking on is named from it. A re-apply of
        // the profile the folder already runs does not start it - that is the apply that should cost
        // nothing.
        using var prefetchCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var prefetch = manifest?.ProfileId != request.ProfileId
            ? GetRegisteredContentAsync(request.RepoId, prefetchCancel.Token)
            : null;
        var prefetchUsed = false;

        try
        {
            // Off the caller's thread, because the part of the scan the manifest cannot answer opens
            // archives, and alongside the request for the mod list, because neither needs the other.
            var installedScan = Task.Run(() => GetInstalledAsync(request.Adapter, target, manifest, cancellationToken), cancellationToken);

            await Task.WhenAll(desiredLoad ?? Task.CompletedTask, installedScan);

            var scanned = await installedScan;

            if (desiredLoad is not null)
            {
                (desired, revision) = await desiredLoad;
            }

            var recorded = (manifest?.Entries ?? [])
                .Select(x => x.FileName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // In a shared folder only what sync installed is the layout's to place by, so a file some
            // other program keeps there never lends a mod its name.
            var layout = LayOut(request, desired, target.Shared
                ? [.. scanned.Mods.Where(x => recorded.Contains(Path.GetFileName(x.Path)))]
                : scanned.Mods);

            var installed = target.Shared
                ? LeaveOthersAlone(scanned, layout, recorded)
                : scanned;

            // Needed only when something is actually going to be removed. It is the one input that
            // needs the repo's mod list, and a re-apply that changes nothing should not pay for it.
            RegisteredContent registered;

            if (NeedsRegisteredContent(desired, installed.Mods, manifest))
            {
                prefetchUsed = true;
                registered = await (prefetch ?? GetRegisteredContentAsync(request.RepoId, cancellationToken));
            }
            else if (prefetch is not null)
            {
                // Nothing to classify with it, but it is already on its way and it is where the mods
                // being installed get their titles from. Names only, so the plan is otherwise exactly
                // what it would have been without it - and a list that failed to arrive costs names.
                prefetchUsed = true;

                try
                {
                    registered = RegisteredContent.None with { Names = (await prefetch).Names };
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested is false)
                {
                    registered = RegisteredContent.None;
                }
            }
            else
            {
                registered = RegisteredContent.None;
            }

            var items = await ModSyncPlanner.PlanAsync(
                desired, layout, installed.Mods, registered, manifest, null, cancellationToken, progress);

            return BuildPlan(request, revision, installed.UnmanagedFileNames, UnlinkWhereNotAllowed(target, items), layout);
        }
        finally
        {
            if (prefetch is not null && prefetchUsed is false)
            {
                // The plan failed before it got that far. Stopped and awaited, so its failure is not
                // left to surface as an unobserved one.
                prefetchCancel.Cancel();

                try
                {
                    await prefetch;
                }
                catch (Exception exception)
                {
                    // Its answer is not wanted, so its failure is only worth a trace.
                    logger.LogDebug(exception, "The unused prefetch of the repo's mod list failed.");
                }
            }
        }
    }

    /// <summary>The adapter's layout for the desired set, checked before anything relies on it.</summary>
    private static ModLayout LayOut(ModSyncRequest request, IReadOnlyList<DesiredMod> desired, IReadOnlyList<InstalledMod> installed)
    {
        var installedNames = new Dictionary<ModKey, string>();

        foreach (var mod in installed)
        {
            installedNames.TryAdd(mod.ModId, Path.GetFileName(mod.Path));
        }

        var layout = request.Adapter.Layout(new ModLayoutContext(
            request.Target,
            [.. desired.Select(x => new ModLayoutMod(x.ModId, x.VersionId, x.ContentHash, x.FileName, installedNames.GetValueOrDefault(x.ModId), x.Locked))]));

        foreach (var placement in layout.Placements)
        {
            if (string.IsNullOrWhiteSpace(placement.FileName) ||
                Path.GetFileName(placement.FileName) != placement.FileName ||
                placement.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidOperationException($"The adapter placed '{placement.ModId}' at '{placement.FileName}', which is not a file name.");
            }
        }

        if (layout.Placements.DistinctBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).Count() != layout.Placements.Count)
        {
            throw new InvalidOperationException("The adapter placed two mods at the same file name.");
        }

        if (desired.Any(x => layout.Placements.Count(p => p.ModId == x.ModId) != 1))
        {
            throw new InvalidOperationException("The adapter's layout does not place every desired mod exactly once.");
        }

        return layout;
    }

    /// <summary>
    /// A shared folder narrowed to what sync may touch: the files it installed, and whatever sits at a
    /// name the layout is about to use. Every other mod in it belongs to somebody else and joins the
    /// files sync leaves alone - even one of a mod the profile pins, which then gets a file of its own
    /// beside it.
    /// </summary>
    /// <remarks>
    /// A file at a placement name is claimed because the install cannot happen around it: it is either
    /// already the right bytes, which the planner keeps, or something in the way, which goes the way
    /// any other replaced file does.
    /// </remarks>
    private static (IReadOnlyList<InstalledMod> Mods, IReadOnlyList<string> UnmanagedFileNames) LeaveOthersAlone(
        (IReadOnlyList<InstalledMod> Mods, IReadOnlyList<string> UnmanagedFileNames) scanned,
        ModLayout layout,
        IReadOnlySet<string> recorded)
    {
        var placed = layout.Placements
            .Select(x => x.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var claimed = new List<InstalledMod>();
        var others = new List<string>();

        foreach (var mod in scanned.Mods)
        {
            var name = Path.GetFileName(mod.Path);

            if (recorded.Contains(name) || placed.Contains(name))
            {
                claimed.Add(mod);
            }
            else
            {
                others.Add(name);
            }
        }

        return (claimed, [.. scanned.UnmanagedFileNames, .. others]);
    }

    /// <summary>
    /// Every kept file that is still a hardlink on a target that does not allow them, turned into a
    /// copy of itself - which is what makes switching a target's hardlinks off take effect for the
    /// files linked before it.
    /// </summary>
    /// <remarks>
    /// A link count that cannot be read is taken as no link. That happens on the filesystems that
    /// cannot link in the first place - a network path, exFAT - so there is nothing to undo there.
    /// </remarks>
    private static IReadOnlyList<ModSyncItem> UnlinkWhereNotAllowed(ModTarget target, IReadOnlyList<ModSyncItem> items)
    {
        if (target.SupportsHardlinks)
        {
            return items;
        }

        return [.. items.Select(x =>
            x.Action is ModSyncAction.Keep or ModSyncAction.Rename &&
            FileLinks.TryGetLinkCount(x.InstalledPath!) is > 1
                ? x with { Action = ModSyncAction.Unlink }
                : x)];
    }

    private ModSyncPlan BuildPlan(
        ModSyncRequest request,
        int? revision,
        IReadOnlyList<string> unmanagedFileNames,
        IReadOnlyList<ModSyncItem> items,
        ModLayout layout)
    {
        var target = request.Target;
        var modFolder = target.Path;

        var servingStore = storeProvider.GetStoreServing(modFolder);
        var allStores = storeProvider.GetAllStores();

        var managedFiles = layout.ManagedFiles.Select(x => fileEditor.Plan(modFolder, x)).ToList();

        // A managed file in the mod folder itself is tracked as one, not as somebody else's file.
        var managedNames = managedFiles
            .Where(x => FileSystemHelper.ArePathsEqual(Path.GetDirectoryName(x.FullPath)!, modFolder))
            .Select(x => Path.GetFileName(x.FullPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        unmanagedFileNames = [.. unmanagedFileNames.Where(x => managedNames.Contains(x) is false)];

        items = [.. items, .. FindBlockingFiles(items, unmanagedFileNames, target)];

        return new ModSyncPlan
        {
            RepoId = request.RepoId,
            ProfileId = request.ProfileId,
            ProfileName = request.ProfileName,
            ProfileRevision = revision,
            Game = request.Game,
            GameName = request.GameName,
            Target = target,
            Items = items,
            Materialization = DecideMaterialization(target, servingStore),
            UnmanagedFileNames = unmanagedFileNames,
            ManagedFiles = managedFiles,
            HashesToFetch = [.. items
                .Where(x => x.Action is ModSyncAction.Install or ModSyncAction.Replace)
                .Select(x => x.DesiredHash)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(x => servingStore.Contains(x) is false)],
            Adapter = request.Adapter,
            ServingStore = servingStore,
            AllStores = allStores.Any(x => FileSystemHelper.ArePathsEqual(x.RootPath, servingStore.RootPath))
                ? allStores
                : [servingStore, .. allStores]
        };
    }


    /// <summary>
    /// Which revision this game's folder is to end up on, refusing the apply outright where a
    /// savegame it is holding says it may not move at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Resolved here rather than at each caller, for the reason the observation is.</b> Every apply
    /// in the app reaches this method, and an apply that quietly installs head under a savegame pinned to
    /// revision 4 is the state the whole design exists to prevent - so the one place all of them pass
    /// through is where the question gets asked. A caller that names a revision has said something
    /// this cannot know better than, and is taken at its word and then checked.
    /// </para>
    /// <para>
    /// The refusal is a backstop and not the explanation. An interface that offers a button and then
    /// throws this has already failed; it consults the same rule beforehand and says what would enable
    /// the apply instead - see docs/10-savegame-profile-binding.md#two-actions-not-one.
    /// </para>
    /// </remarks>
    /// <exception cref="UserFriendlyException">A savegame held here refuses this apply.</exception>
    private int? ResolveTargetRevision(ModSyncRequest request)
    {
        var revision = request.Revision ?? heldSavegames.GetRequiredRevision(request.Game, request.ProfileId);
        var decision = heldSavegames.DecideApply(request.Game, request.ProfileId, revision);

        return decision.Refusal switch
        {
            SavegameApplyRefusal.None => revision,

            SavegameApplyRefusal.AnotherProfileIsHeld => throw new UserFriendlyException(
                "A savegame checked out here follows another mod list",
                $"Game '{request.Game}' is holding savegame '{decision.SavegameId}', which follows profile '{decision.ProfileId}'. Applying '{request.ProfileId}' would take that savegame off the mod list it runs on. Check it in first."),

            _ => throw new UserFriendlyException(
                "That savegame runs on one revision, and this is not it",
                $"Game '{request.Game}' is holding savegame '{decision.SavegameId}', checked out in compatibility mode on revision {decision.Revision} of profile '{decision.ProfileId}'. Revision {revision} was asked for; only {decision.Revision} may be applied while it is held.")
        };
    }

    /// <summary>
    /// The backstop for clearing a folder, for the reason <see cref="ResolveTargetRevision"/> is one
    /// for applying: a savegame that follows a mod list must not have the list emptied from under it,
    /// and every route to this method passes through here.
    /// </summary>
    /// <exception cref="UserFriendlyException">A savegame checked out here follows a mod list.</exception>
    private void RefuseClearWhileHeld(ModSyncRequest request)
    {
        if (heldSavegames.FindProfileHold(request.Game) is SavegameCheckoutBinding held)
        {
            throw new UserFriendlyException(
                "A savegame checked out here follows a mod list",
                $"Game '{request.Game}' is holding savegame '{held.SavegameId}', which follows a profile. Clearing the mod folder would take that savegame off the mod list it runs on. Check it in first.");
        }
    }

    /// <summary>
    /// What the profile pins at the requested revision, and which revision that is - recorded in the
    /// manifest so a folder can say which version of the list it was made to match.
    /// </summary>
    /// <remarks>
    /// The revision is answered back rather than assumed from what was asked, because null means head
    /// and only the server knows which number that is.
    /// </remarks>
    private async Task<(IReadOnlyList<DesiredMod> Mods, int Revision)> GetDesiredAsync(
        ModSyncRequest request, int? revision, CancellationToken cancellationToken)
    {
        var response = await modDependenciesClient.GetModDependenciesV1Async(request.RepoId, request.ProfileId, revision, cancellationToken);

        // Normalized where the ids enter the client, as everywhere else, and the file name checked
        // in the same breath: it came off somebody else's disk and is about to be interpolated into
        // a path here, so it is validated against the id it arrived with rather than trusted.
        return ([.. response.Dependencies.Select(x =>
        {
            var modId = ModKey.From(x.ModId);

            return new DesiredMod(
                modId,
                ModVersionKey.From(x.ModVersionId),
                x.ContentHash,
                x.Locked)
            {
                FileName = ModFileName.For(modId, x.FileName),
                SizeBytes = x.SizeBytes
            };
        })], response.Revision);
    }

    /// <remarks>
    /// <b>A file the manifest still describes is not opened.</b> Its size and modification time are
    /// what the last apply left, so it is the file that apply installed - the same check the planner
    /// trusts the recorded hash on - and the manifest already says which mod and version it is. Only
    /// the rest go to the adapter, which on an ordinary activation is none of them rather than the
    /// thousand archives the folder holds.
    /// </remarks>
    private static async Task<(IReadOnlyList<InstalledMod> Mods, IReadOnlyList<string> UnmanagedFileNames)> GetInstalledAsync(
        ILocalModAdapter adapter,
        ModTarget target,
        SyncManifest? manifest,
        CancellationToken cancellationToken)
    {
        var mods = new List<InstalledMod>();
        var recognised = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recorded = (manifest?.Entries ?? []).ToDictionary(x => x.FileName, StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(target.Path))
        {
            var info = new FileInfo(file);

            if (recorded.TryGetValue(info.Name, out var entry) is false ||
                entry.Size != info.Length ||
                entry.ModifiedUtc != info.LastWriteTimeUtc)
            {
                continue;
            }

            known.Add(file);
            recognised.Add(info.Name);
            mods.Add(new InstalledMod(
                ModKey.From(entry.ModId),
                ModVersionKey.From(entry.VersionId),
                file,
                entry.DisplayName ?? entry.ModId,
                info.Length,
                info.LastWriteTimeUtc));
        }

        var found = await adapter.GetInstalledMods(target, known.Contains, cancellationToken);

        foreach (var mod in found)
        {
            var info = new FileInfo(mod.FilePath);

            if (info.Exists is false)
            {
                continue;
            }

            recognised.Add(info.Name);
            mods.Add(new InstalledMod(mod.Id, mod.Version, mod.FilePath, mod.Name, info.Length, info.LastWriteTimeUtc));
        }

        // Everything else in the folder is somebody else's business - a readme, a log, an archive
        // that is not a mod. Recorded so that drift detection does not report them as additions
        // forever, and otherwise never touched.
        var unmanaged = Directory.EnumerateFiles(target.Path)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(x => recognised.Contains(x) is false)
            .ToList();

        return (mods, unmanaged);
    }

    /// <summary>
    /// Files the adapter does not recognise that sit exactly where a mod is about to be installed -
    /// a corrupt download under the right name is the ordinary way this happens. They are the user's
    /// files, so they are quarantined rather than overwritten.
    /// </summary>
    private static IReadOnlyList<ModSyncItem> FindBlockingFiles(
        IReadOnlyList<ModSyncItem> items,
        IReadOnlyList<string> unmanagedFileNames,
        ModTarget target)
    {
        if (unmanagedFileNames.Count == 0)
        {
            return [];
        }

        var unmanaged = new HashSet<string>(unmanagedFileNames, StringComparer.OrdinalIgnoreCase);
        var blocking = new List<ModSyncItem>();

        foreach (var item in items.Where(x => x.Action is ModSyncAction.Install or ModSyncAction.Replace or ModSyncAction.Rename or ModSyncAction.Unlink))
        {
            var name = item.FileName!;

            if (unmanaged.Remove(name) is false)
            {
                continue;
            }

            blocking.Add(new ModSyncItem
            {
                Action = ModSyncAction.Quarantine,
                ModId = item.ModId,
                DisplayName = name,
                InstalledPath = Path.Combine(target.Path, name),
                InstalledIsRecoverable = false
            });
        }

        return blocking;
    }

    /// <summary>
    /// Whether anything is going to be removed, which is the only thing the repo's mod list is needed
    /// for. Answered from the manifest, using exactly the check the planner uses, so the two cannot
    /// disagree about which files match.
    /// </summary>
    private static bool NeedsRegisteredContent(
        IReadOnlyCollection<DesiredMod> desired,
        IReadOnlyCollection<InstalledMod> installed,
        SyncManifest? manifest)
    {
        if (installed.Count == 0)
        {
            return false;
        }

        var wanted = desired.ToDictionary(x => x.ModId);
        var recorded = (manifest?.Entries ?? []).ToDictionary(x => x.FileName, StringComparer.OrdinalIgnoreCase);

        foreach (var mod in installed)
        {
            if (wanted.TryGetValue(mod.ModId, out var want) is false)
            {
                return true;
            }

            if (recorded.TryGetValue(Path.GetFileName(mod.Path), out var entry) is false ||
                entry.Size != mod.Size ||
                entry.ModifiedUtc != mod.ModifiedUtc ||
                ModContentHasher.Matches(entry.ContentHash, want.ContentHash) is false)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<RegisteredContent> GetRegisteredContentAsync(Guid repoId, CancellationToken cancellationToken)
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? cursor = null;

        do
        {
            var page = await modsClient.GetModsV1Async(repoId, null, cursor, _registeredPageSize, cancellationToken);

            foreach (var mod in page.Mods)
            {
                hashes.Add(mod.ContentHash);
                names.TryAdd(mod.ContentHash, mod.DisplayName);
            }

            cursor = page.NextCursor;
        }
        while (string.IsNullOrEmpty(cursor) is false);

        return new RegisteredContent(hashes) { Names = names };
    }

    /// <summary>
    /// Hardlink where the disk is served by its own store and the target says nothing writing to it
    /// rewrites a mod file in place; a copy in every other case.
    /// </summary>
    /// <remarks>
    /// The fallback is probed rather than assumed, in the store's own temporary folder - same volume,
    /// therefore same filesystem, and nothing is written into the folder the game owns. Only a
    /// same-disk assignment that falls back is worth warning about: a cross-disk store is a
    /// deliberate trade of sync time for space, and a target without hardlink support is a stated
    /// property of the game rather than a silent surprise.
    /// </remarks>
    private ModMaterialization DecideMaterialization(ModTarget target, ContentStore servingStore)
    {
        var sameVolume = string.Equals(
            FileSystemHelper.NormalizeVolumeRoot(target.Path),
            FileSystemHelper.NormalizeVolumeRoot(servingStore.RootPath),
            StringComparison.OrdinalIgnoreCase);

        if (sameVolume is false || target.SupportsHardlinks is false)
        {
            return new ModMaterialization(MaterializationMethod.Copy, FellBackToCopy: false);
        }

        return SupportsHardlinks(servingStore)
            ? new ModMaterialization(MaterializationMethod.Hardlink, FellBackToCopy: false)
            : new ModMaterialization(MaterializationMethod.Copy, FellBackToCopy: true);
    }

    private bool SupportsHardlinks(ContentStore servingStore)
    {
        var probe = string.Empty;
        var link = string.Empty;

        try
        {
            probe = servingStore.CreateTemporaryPath(".linkprobe");
            link = probe + ".link";

            File.WriteAllBytes(probe, []);

            return FileLinks.TryCreateHardLink(link, probe);
        }
        catch (Exception exception)
        {
            // A store that cannot be hardlinked into is copied into instead, which is slower and
            // correct - and indistinguishable from a store that simply chose to copy.
            logger.LogInformation(exception, "Hardlink probe failed in {Store}; falling back to copying.", servingStore.RootPath);

            return false;
        }
        finally
        {
            if (probe.Length > 0)
            {
                FileSystemHelper.TryDeleteFile(link, logger);
                FileSystemHelper.TryDeleteFile(probe, logger);
            }
        }
    }
}
