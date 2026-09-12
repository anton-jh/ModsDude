using ModsDude.Client.Core.GameAdapters;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Tests.Sync;

public class DriftServiceTests
{
    private static readonly Guid _repoId = Guid.NewGuid();
    private static readonly Guid _profileId = Guid.NewGuid();


    [Fact]
    public void A_folder_that_still_matches_the_manifest_is_in_sync()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "one"), ("fs25_b.zip", "two"));

        Assert.Equal(DriftStatus.InSync, fixture.Check().Status);
    }

    [Fact]
    public void A_replaced_file_is_drift()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        // What an in-game update-all leaves behind: same name, different contents, and the manifest
        // frozen at what sync installed.
        fixture.Folder.WriteFile("fs25_a.zip", "the game updated this");

        var report = fixture.Check();

        Assert.Equal(DriftStatus.Drifted, report.Status);
        Assert.Equal(["fs25_a.zip"], report.Changed);
    }

    [Fact]
    public void An_added_or_removed_file_is_drift()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "one"), ("fs25_b.zip", "two"));

        File.Delete(fixture.Folder.Combine("fs25_b.zip"));
        fixture.Folder.WriteFile("fs25_c.zip", "installed from inside the game");

        var report = fixture.Check();

        Assert.Equal(DriftStatus.Drifted, report.Status);
        Assert.Equal(["fs25_c.zip"], report.Added);
        Assert.Equal(["fs25_b.zip"], report.Removed);
    }

    [Fact]
    public void A_game_with_no_active_profile_has_nothing_to_drift_from()
    {
        using var fixture = new DriftFixture();

        Assert.Equal(
            DriftStatus.NoActiveProfile,
            fixture.Service.Check(fixture.Game, null, fixture.Folder.Path).Status);
    }

    [Fact]
    public void A_deleted_profile_is_said_so_rather_than_reported_as_drift()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        var report = fixture.Service.Check(
            fixture.Game,
            new ActiveProfile(_repoId, _profileId),
            fixture.Folder.Path,
            profileIsMissing: true);

        Assert.Equal(DriftStatus.DanglingProfile, report.Status);
    }

    [Fact]
    public void An_active_profile_with_no_manifest_is_unknown_rather_than_drifted()
    {
        using var fixture = new DriftFixture();
        fixture.Folder.WriteFile("fs25_a.zip", "one");

        Assert.Equal(DriftStatus.NeverSynced, fixture.Check().Status);
    }

    [Fact]
    public void A_manifest_for_a_different_profile_says_nothing_about_this_one()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        var report = fixture.Service.Check(
            fixture.Game,
            new ActiveProfile(_repoId, Guid.NewGuid()),
            fixture.Folder.Path);

        Assert.Equal(DriftStatus.NeverSynced, report.Status);
    }

    [Fact]
    public void An_unreachable_folder_is_unknown_not_drifted()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        // An unplugged drive or an offline network path. Warning about mods that may be perfectly
        // fine is worse than saying nothing.
        var report = fixture.Service.Check(
            fixture.Game,
            new ActiveProfile(_repoId, _profileId),
            fixture.Folder.Combine("gone"));

        Assert.Equal(DriftStatus.FolderUnreachable, report.Status);
    }

    [Fact]
    public void Someone_else_editing_the_shared_profile_is_drift_too()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        // The folder is untouched; the profile moved on. No revision number on the profile is
        // needed - the applied mod set is what the comparison is against.
        var report = fixture.Service.Check(
            fixture.Game,
            new ActiveProfile(_repoId, _profileId),
            fixture.Folder.Path,
            profileDependencies: [
                new DesiredMod(ModKey.From("fs25_a"), ModVersionKey.From("2.0.0"), HashOf("a newer build"), Locked: true)]);

        Assert.Equal(DriftStatus.Drifted, report.Status);
        Assert.Equal([ModKey.From("fs25_a")], report.ProfileChangedMods);

        // A locked mod at the wrong version is a damaged savegame waiting to happen, so it is named
        // rather than folded into a count.
        Assert.Equal([ModKey.From("fs25_a")], report.LockedMods);
    }

    [Fact]
    public void A_profile_that_still_pins_what_was_applied_is_not_drift()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        var report = fixture.Service.Check(
            fixture.Game,
            new ActiveProfile(_repoId, _profileId),
            fixture.Folder.Path,
            profileDependencies: [
                new DesiredMod(ModKey.From("fs25_a"), ModVersionKey.From("1.0.0"), HashOf("one"), Locked: false)]);

        Assert.Equal(DriftStatus.InSync, report.Status);
    }


    [Fact]
    public void A_locked_mod_the_game_replaced_is_named_rather_than_counted()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_map.zip", "one", true, "Great Plains 16x"), ("fs25_a.zip", "two", false, "A Trailer"));

        // What an in-game update-all does to a map. The profile's dependencies are not in hand here -
        // this is the startup path - so the lock comes off the manifest.
        fixture.Folder.WriteFile("fs25_map.zip", "the game updated this");
        fixture.Folder.WriteFile("fs25_a.zip", "and this");

        var report = fixture.Check();

        var locked = Assert.Single(report.LockedDrift);

        Assert.Equal(ModKey.From("fs25_map"), locked.ModId);
        Assert.Equal("Great Plains 16x", locked.DisplayName);
        Assert.Equal("1.0.0", locked.AppliedVersion);
        Assert.Equal(LockedDriftReason.FileChanged, locked.Reason);
    }

    [Fact]
    public void An_unlocked_mod_at_the_wrong_version_is_untidy_and_not_called_out()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "two", false, "A Trailer"));

        fixture.Folder.WriteFile("fs25_a.zip", "the game updated this");

        var report = fixture.Check();

        Assert.Equal(DriftStatus.Drifted, report.Status);
        Assert.Empty(report.LockedDrift);
    }

    [Fact]
    public void A_locked_mod_that_left_the_folder_is_named_with_that_reason()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_map.zip", "one", true, "Great Plains 16x"));

        File.Delete(fixture.Folder.Combine("fs25_map.zip"));

        var locked = Assert.Single(fixture.Check().LockedDrift);

        Assert.Equal(LockedDriftReason.FileRemoved, locked.Reason);
    }

    [Fact]
    public void One_locked_mod_gone_wrong_two_ways_is_still_one_problem()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_map.zip", "one", true, "Great Plains 16x"));

        fixture.Folder.WriteFile("fs25_map.zip", "the game updated this");

        var report = fixture.Service.Check(
            fixture.Game,
            new ActiveProfile(_repoId, _profileId),
            fixture.Folder.Path,
            profileDependencies: [
                new DesiredMod(ModKey.From("fs25_map"), ModVersionKey.From("2.0.0"), HashOf("something else"), Locked: true)]);

        var locked = Assert.Single(report.LockedDrift);

        // The file is the half already on disk, so that is the one reported.
        Assert.Equal(LockedDriftReason.FileChanged, locked.Reason);
        Assert.Equal([ModKey.From("fs25_map")], report.LockedMods);
    }


    /// <summary>
    /// A file the listing held and the comparison could not read: drift, not an exception.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The race this is about is real and was reached in the field - an in-game update-all replaces a
    /// mod between the directory listing and the size read, and <c>FileInfo.Length</c> throws
    /// <c>FileNotFoundException</c> for a name that was listed a moment earlier. It used to take the
    /// whole check with it, every other game's answer included, and arrive as an unobserved task
    /// exception from a background thread.
    /// </para>
    /// <para>
    /// Staged by handing in the listing rather than by writing files, because the whole point is the
    /// gap between the two reads and no test can get inside a real
    /// <see cref="Directory.EnumerateFiles"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_listed_file_that_has_gone_by_the_time_it_is_read_is_changed()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "one"), ("fs25_b.zip", "two"));

        // What the update-all did in the gap. The listing still names it, because the listing was
        // taken before.
        File.Delete(fixture.Folder.Combine("fs25_a.zip"));

        var (added, removed, changed) = DriftService.CompareFolder(
            fixture.Manifests.TryRead(fixture.Game)!,
            ["fs25_a.zip", "fs25_b.zip"],
            fixture.Folder.Path);

        // Changed rather than removed: as far as this comparison was told, the file is there. Either
        // way the folder no longer holds what was applied, and a re-apply is what fixes it.
        Assert.Equal(["fs25_a.zip"], changed);
        Assert.Empty(added);
        Assert.Empty(removed);
    }


    private static string HashOf(string content)
        => ModContentHasher.Format(SHA256.HashData(Encoding.UTF8.GetBytes(content)));


    private sealed class DriftFixture : IDisposable
    {
        private readonly TempDirectory _manifests = new("drift-manifests");


        public DriftFixture()
        {
            Manifests = new SyncManifestStore(_manifests.Path);
            Service = new DriftService(Manifests, NullLogger<DriftService>.Instance);
        }


        public TempDirectory Folder { get; } = new("drift-mods");
        public SyncManifestStore Manifests { get; }
        public DriftService Service { get; }
        public GameIdentity Game { get; } = Keys.Game();


        /// <summary>Writes the files and the manifest that says they are what was installed.</summary>
        public void Sync(params (string Name, string Content)[] files)
            => Sync([.. files.Select(x => (x.Name, x.Content, false, (string?)null))]);

        public void Sync(params (string Name, string Content, bool Locked, string? DisplayName)[] files)
        {
            var entries = new List<SyncManifestEntry>();

            foreach (var (name, content, locked, displayName) in files)
            {
                var path = Folder.WriteFile(name, content);
                var info = new FileInfo(path);

                entries.Add(new SyncManifestEntry(
                    Path.GetFileNameWithoutExtension(name),
                    "1.0.0",
                    HashOf(content),
                    name,
                    info.Length,
                    info.LastWriteTimeUtc)
                {
                    Locked = locked,
                    DisplayName = displayName
                });
            }

            Manifests.Write(new SyncManifest
            {
                Game = Game,
                RepoId = _repoId,
                ProfileId = _profileId,
                SyncedAt = DateTimeOffset.UtcNow,
                ModFolder = Folder.Path,
                Entries = entries
            });
        }

        public DriftReport Check()
            => Service.Check(Game, new ActiveProfile(_repoId, _profileId), Folder.Path);

        public void Dispose()
        {
            Folder.Dispose();
            _manifests.Dispose();
        }
    }
}
