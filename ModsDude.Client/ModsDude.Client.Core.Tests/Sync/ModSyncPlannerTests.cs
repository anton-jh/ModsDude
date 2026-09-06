using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Tests.Sync;

public class ModSyncPlannerTests
{
    [Fact]
    public async Task A_pinned_mod_that_is_absent_is_installed()
    {
        var items = await Plan([Want("fs25_a", "1.0.0", "the bytes")], [], RegisteredContent.None, null);

        var item = Assert.Single(items);

        Assert.Equal(ModSyncAction.Install, item.Action);
        Assert.Equal("1.0.0", item.DesiredVersion?.Value);
    }

    [Fact]
    public async Task A_pinned_mod_whose_bytes_already_match_is_kept()
    {
        using var folder = new TempDirectory("plan-keep");
        var installed = Install(folder, "fs25_a", "1.0.0", "the bytes");

        var items = await Plan([Want("fs25_a", "1.0.0", "the bytes")], [installed], RegisteredContent.None, null);

        Assert.Equal(ModSyncAction.Keep, Assert.Single(items).Action);
    }

    /// <summary>
    /// The case the whole classification-on-bytes rule exists for: two builds calling themselves the
    /// same version are indistinguishable to the adapter, which reads the version out of the mod's
    /// own metadata. Without hashing, this would be classified Keep and the wrong file would stay.
    /// </summary>
    [Fact]
    public async Task Same_version_with_different_bytes_is_replaced()
    {
        using var folder = new TempDirectory("plan-same-version");
        var installed = Install(folder, "fs25_a", "1.0.0", "one build of 1.0.0");

        var items = await Plan(
            [Want("fs25_a", "1.0.0", "a different build of 1.0.0")],
            [installed],
            RegisteredContent.None,
            null);

        var item = Assert.Single(items);

        Assert.Equal(ModSyncAction.Replace, item.Action);
        Assert.Equal(HashOf("one build of 1.0.0"), item.InstalledHash);
    }

    [Fact]
    public async Task An_unpinned_mod_the_repo_can_reproduce_is_uninstalled()
    {
        using var folder = new TempDirectory("plan-uninstall");
        var installed = Install(folder, "fs25_old", "1.0.0", "registered content");

        var items = await Plan([], [installed], Registered("registered content"), null);

        var item = Assert.Single(items);

        Assert.Equal(ModSyncAction.UninstallRecoverable, item.Action);
        Assert.True(item.InstalledIsRecoverable);
        Assert.False(item.DestroysUnrecognisedFile);
    }

    [Fact]
    public async Task An_unpinned_mod_nothing_has_a_copy_of_is_quarantined()
    {
        using var folder = new TempDirectory("plan-quarantine");
        var installed = Install(folder, "fs25_mine", "1.0.0", "the user's own file");

        var items = await Plan([], [installed], Registered("something else"), null);

        var item = Assert.Single(items);

        Assert.Equal(ModSyncAction.Quarantine, item.Action);
        Assert.True(item.DestroysUnrecognisedFile);
    }

    /// <summary>
    /// Recoverability is a property of the bytes, not of the version id: a file wearing a registered
    /// version id while containing something else cannot be fetched again, so deleting it would lose
    /// it.
    /// </summary>
    [Fact]
    public async Task A_registered_version_id_with_unregistered_bytes_is_still_quarantined()
    {
        using var folder = new TempDirectory("plan-imposter");
        var installed = Install(folder, "fs25_a", "1.0.0", "a build the repo has never seen");

        var items = await Plan([], [installed], Registered("the build the repo actually holds"), null);

        Assert.Equal(ModSyncAction.Quarantine, Assert.Single(items).Action);
    }

    [Fact]
    public async Task A_replaced_file_the_repo_has_never_seen_is_treated_as_unrecognised()
    {
        using var folder = new TempDirectory("plan-replace-unknown");
        var installed = Install(folder, "fs25_a", "0.9.0", "a build from somewhere else");

        var items = await Plan(
            [Want("fs25_a", "1.0.0", "the pinned build")],
            [installed],
            Registered("the pinned build"),
            null);

        var item = Assert.Single(items);

        Assert.Equal(ModSyncAction.Replace, item.Action);
        Assert.False(item.InstalledIsRecoverable);
        Assert.True(item.DestroysUnrecognisedFile);
    }

    [Fact]
    public async Task The_manifest_answers_for_a_file_that_has_not_changed()
    {
        using var folder = new TempDirectory("plan-manifest-hit");
        var installed = Install(folder, "fs25_a", "1.0.0", "the bytes");
        var manifest = ManifestFor(installed, HashOf("the bytes"));

        var hashed = new List<string>();

        var items = await Plan(
            [Want("fs25_a", "1.0.0", "the bytes")],
            [installed],
            RegisteredContent.None,
            manifest,
            (path, ct) =>
            {
                hashed.Add(path);

                return ContentStore.HashFileAsync(path, ct);
            });

        Assert.Equal(ModSyncAction.Keep, Assert.Single(items).Action);

        // The point of the manifest: a file whose size and time still match it is not opened at all.
        Assert.Empty(hashed);
    }

    [Fact]
    public async Task A_file_whose_stat_no_longer_matches_the_manifest_is_rehashed()
    {
        using var folder = new TempDirectory("plan-manifest-miss");
        var installed = Install(folder, "fs25_a", "1.0.0", "the game updated this");

        // The manifest still describes what sync installed; the file on disk is something else, which
        // is exactly what an in-game update leaves behind.
        var manifest = new SyncManifest
        {
            InstanceId = Guid.NewGuid(),
            RepoId = Guid.NewGuid(),
            ProfileId = Guid.NewGuid(),
            SyncedAt = DateTimeOffset.UtcNow,
            ModFolder = folder.Path,
            Entries = [new SyncManifestEntry(
                "fs25_a",
                "1.0.0",
                HashOf("what sync installed"),
                Path.GetFileName(installed.Path),
                Size: 12,
                ModifiedUtc: DateTimeOffset.UnixEpoch)]
        };

        var hashed = new List<string>();

        var items = await Plan(
            [Want("fs25_a", "1.0.0", "what sync installed")],
            [installed],
            RegisteredContent.None,
            manifest,
            (path, ct) =>
            {
                hashed.Add(path);

                return ContentStore.HashFileAsync(path, ct);
            });

        Assert.Equal(ModSyncAction.Replace, Assert.Single(items).Action);
        Assert.Equal([installed.Path], hashed);
    }

    [Fact]
    public async Task A_second_file_for_the_same_mod_is_not_left_installed()
    {
        using var folder = new TempDirectory("plan-duplicate");
        var first = Install(folder, "fs25_a", "1.0.0", "the pinned build");
        var duplicate = new InstalledMod(
            ModKey.From("fs25_a"),
            ModVersionKey.From("1.0.0"),
            folder.WriteFile("fs25_a copy.zip", "an older download"),
            "Mod A",
            "an older download".Length,
            File.GetLastWriteTimeUtc(folder.Combine("fs25_a copy.zip")));

        var items = await Plan(
            [Want("fs25_a", "1.0.0", "the pinned build")],
            [first, duplicate],
            RegisteredContent.None,
            null);

        Assert.Equal(ModSyncAction.Keep, items.Single(x => x.InstalledPath == first.Path).Action);
        Assert.Equal(ModSyncAction.Quarantine, items.Single(x => x.InstalledPath == duplicate.Path).Action);
    }


    /// <summary>
    /// What a folder an older client lower-cased looks like on the next apply. The bytes are right,
    /// so nothing is fetched or removed - but the name is not what the repo registered, and leaving
    /// it would mean the correction could never happen.
    /// </summary>
    [Fact]
    public async Task A_matching_file_under_the_wrong_name_is_renamed()
    {
        using var folder = new TempDirectory("plan-rename");
        var installed = Install(folder, "fs25_a", "1.0.0", "the bytes");

        var items = await Plan(
            [Want("fs25_a", "1.0.0", "the bytes") with { FileName = ModFileName.For(Keys.Mod("fs25_a"), "FS25_A.zip") }],
            [installed],
            RegisteredContent.None,
            null);

        var item = Assert.Single(items);

        Assert.Equal(ModSyncAction.Rename, item.Action);
        Assert.Equal("FS25_A.zip", item.FileName?.Value);
    }

    [Fact]
    public async Task A_matching_file_already_under_the_registered_name_is_kept()
    {
        using var folder = new TempDirectory("plan-named-right");
        var installed = Install(folder, "fs25_a", "1.0.0", "the bytes");

        var items = await Plan(
            [Want("fs25_a", "1.0.0", "the bytes") with { FileName = ModFileName.For(Keys.Mod("fs25_a"), "fs25_a.zip") }],
            [installed],
            RegisteredContent.None,
            null);

        Assert.Equal(ModSyncAction.Keep, Assert.Single(items).Action);
    }

    /// <summary>
    /// A repo with nothing usable registered has no opinion about the name, so the file is left
    /// exactly as it is rather than renamed to the id.
    /// </summary>
    [Fact]
    public async Task A_repo_that_registered_no_name_renames_nothing()
    {
        using var folder = new TempDirectory("plan-no-name");
        var installed = Install(folder, "FS25_A", "1.0.0", "the bytes");

        var items = await Plan([Want("fs25_a", "1.0.0", "the bytes")], [installed], RegisteredContent.None, null);

        Assert.Equal(ModSyncAction.Keep, Assert.Single(items).Action);
    }

    /// <summary>
    /// A name is only ever compared for a file whose bytes already match, so a mod that renames
    /// itself between versions arrives as different content and stays a replace.
    /// </summary>
    [Fact]
    public async Task Wrong_bytes_under_the_registered_name_are_still_replaced()
    {
        using var folder = new TempDirectory("plan-rename-vs-replace");
        var installed = Install(folder, "fs25_a", "1.0.0", "the old build");

        var items = await Plan(
            [Want("fs25_a", "1.0.0", "the new build") with { FileName = ModFileName.For(Keys.Mod("fs25_a"), "FS25_A.zip") }],
            [installed],
            RegisteredContent.None,
            null);

        Assert.Equal(ModSyncAction.Replace, Assert.Single(items).Action);
    }


    private static Task<IReadOnlyList<ModSyncItem>> Plan(
        IReadOnlyCollection<DesiredMod> desired,
        IReadOnlyCollection<InstalledMod> installed,
        RegisteredContent registered,
        SyncManifest? manifest,
        Func<string, CancellationToken, Task<string>>? hashFile = null)
    {
        return ModSyncPlanner.PlanAsync(desired, installed, registered, manifest, hashFile, CancellationToken.None);
    }

    private static DesiredMod Want(string modId, string version, string content, bool locked = false)
        => new(ModKey.From(modId), ModVersionKey.From(version), HashOf(content), locked);

    private static InstalledMod Install(TempDirectory folder, string modId, string version, string content)
    {
        var path = folder.WriteFile($"{modId}.zip", content);
        var info = new FileInfo(path);

        return new InstalledMod(ModKey.From(modId), ModVersionKey.From(version), path, modId, info.Length, info.LastWriteTimeUtc);
    }

    private static SyncManifest ManifestFor(InstalledMod installed, string hash)
    {
        return new SyncManifest
        {
            InstanceId = Guid.NewGuid(),
            RepoId = Guid.NewGuid(),
            ProfileId = Guid.NewGuid(),
            SyncedAt = DateTimeOffset.UtcNow,
            ModFolder = Path.GetDirectoryName(installed.Path)!,
            Entries = [new SyncManifestEntry(
                installed.ModId.Value,
                installed.VersionId.Value,
                hash,
                Path.GetFileName(installed.Path),
                installed.Size,
                installed.ModifiedUtc)]
        };
    }

    private static RegisteredContent Registered(params string[] contents)
        => new(new HashSet<string>(contents.Select(HashOf), StringComparer.OrdinalIgnoreCase));

    private static string HashOf(string content)
        => ModContentHasher.Format(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
