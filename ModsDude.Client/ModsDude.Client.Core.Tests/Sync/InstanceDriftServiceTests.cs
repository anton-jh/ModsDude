using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Tests.Sync;

public class InstanceDriftServiceTests
{
    private static readonly Guid _repoId = Guid.NewGuid();
    private static readonly Guid _profileId = Guid.NewGuid();


    [Fact]
    public void A_folder_that_still_matches_the_manifest_is_in_sync()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "one"), ("fs25_b.zip", "two"));

        Assert.Equal(InstanceDriftStatus.InSync, fixture.Check().Status);
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

        Assert.Equal(InstanceDriftStatus.Drifted, report.Status);
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

        Assert.Equal(InstanceDriftStatus.Drifted, report.Status);
        Assert.Equal(["fs25_c.zip"], report.Added);
        Assert.Equal(["fs25_b.zip"], report.Removed);
    }

    [Fact]
    public void An_instance_with_no_active_profile_has_nothing_to_drift_from()
    {
        using var fixture = new DriftFixture();

        Assert.Equal(
            InstanceDriftStatus.NoActiveProfile,
            fixture.Service.Check(fixture.InstanceId, null, fixture.Folder.Path).Status);
    }

    [Fact]
    public void A_deleted_profile_is_said_so_rather_than_reported_as_drift()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        var report = fixture.Service.Check(
            fixture.InstanceId,
            new ActiveProfile(_repoId, _profileId),
            fixture.Folder.Path,
            profileIsMissing: true);

        Assert.Equal(InstanceDriftStatus.DanglingProfile, report.Status);
    }

    [Fact]
    public void An_active_profile_with_no_manifest_is_unknown_rather_than_drifted()
    {
        using var fixture = new DriftFixture();
        fixture.Folder.WriteFile("fs25_a.zip", "one");

        Assert.Equal(InstanceDriftStatus.NeverSynced, fixture.Check().Status);
    }

    [Fact]
    public void A_manifest_for_a_different_profile_says_nothing_about_this_one()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        var report = fixture.Service.Check(
            fixture.InstanceId,
            new ActiveProfile(_repoId, Guid.NewGuid()),
            fixture.Folder.Path);

        Assert.Equal(InstanceDriftStatus.NeverSynced, report.Status);
    }

    [Fact]
    public void An_unreachable_folder_is_unknown_not_drifted()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        // An unplugged drive or an offline network path. Warning about mods that may be perfectly
        // fine is worse than saying nothing.
        var report = fixture.Service.Check(
            fixture.InstanceId,
            new ActiveProfile(_repoId, _profileId),
            fixture.Folder.Combine("gone"));

        Assert.Equal(InstanceDriftStatus.FolderUnreachable, report.Status);
    }

    [Fact]
    public void Someone_else_editing_the_shared_profile_is_drift_too()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        // The folder is untouched; the profile moved on. No revision number on the profile is
        // needed - the applied mod set is what the comparison is against.
        var report = fixture.Service.Check(
            fixture.InstanceId,
            new ActiveProfile(_repoId, _profileId),
            fixture.Folder.Path,
            profileDependencies: [
                new DesiredMod(ModKey.From("fs25_a"), ModVersionKey.From("2.0.0"), HashOf("a newer build"), Locked: true)]);

        Assert.Equal(InstanceDriftStatus.Drifted, report.Status);
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
            fixture.InstanceId,
            new ActiveProfile(_repoId, _profileId),
            fixture.Folder.Path,
            profileDependencies: [
                new DesiredMod(ModKey.From("fs25_a"), ModVersionKey.From("1.0.0"), HashOf("one"), Locked: false)]);

        Assert.Equal(InstanceDriftStatus.InSync, report.Status);
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

        Assert.Equal(InstanceDriftStatus.Drifted, report.Status);
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
            fixture.InstanceId,
            new ActiveProfile(_repoId, _profileId),
            fixture.Folder.Path,
            profileDependencies: [
                new DesiredMod(ModKey.From("fs25_map"), ModVersionKey.From("2.0.0"), HashOf("something else"), Locked: true)]);

        var locked = Assert.Single(report.LockedDrift);

        // The file is the half already on disk, so that is the one reported.
        Assert.Equal(LockedDriftReason.FileChanged, locked.Reason);
        Assert.Equal([ModKey.From("fs25_map")], report.LockedMods);
    }


    private static string HashOf(string content)
        => ModContentHasher.Format(SHA256.HashData(Encoding.UTF8.GetBytes(content)));


    private sealed class DriftFixture : IDisposable
    {
        private readonly TempDirectory _manifests = new("drift-manifests");


        public DriftFixture()
        {
            Manifests = new SyncManifestStore(_manifests.Path);
            Service = new InstanceDriftService(Manifests, NullLogger<InstanceDriftService>.Instance);
        }


        public TempDirectory Folder { get; } = new("drift-mods");
        public SyncManifestStore Manifests { get; }
        public InstanceDriftService Service { get; }
        public Guid InstanceId { get; } = Guid.NewGuid();


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
                InstanceId = InstanceId,
                RepoId = _repoId,
                ProfileId = _profileId,
                SyncedAt = DateTimeOffset.UtcNow,
                ModFolder = Folder.Path,
                Entries = entries
            });
        }

        public InstanceDriftReport Check()
            => Service.Check(InstanceId, new ActiveProfile(_repoId, _profileId), Folder.Path);

        public void Dispose()
        {
            Folder.Dispose();
            _manifests.Dispose();
        }
    }
}
