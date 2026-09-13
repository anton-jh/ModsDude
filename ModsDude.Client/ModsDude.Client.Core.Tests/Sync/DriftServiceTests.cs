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
            fixture.Service.Check(fixture.Target, null, fixture.Folder.Path).Status);
    }

    [Fact]
    public void A_deleted_profile_is_said_so_rather_than_reported_as_drift()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        var report = fixture.Service.Check(
            fixture.Target,
            new ActiveProfile(_repoId, _profileId),
            fixture.Folder.Path,
            profileIsMissing: true);

        Assert.Equal(DriftStatus.DanglingProfile, report.Status);
    }

    /// <summary>
    /// An intent standing with no record of any work behind it. The comparison cannot be made -
    /// there is nothing to compare against - and that is itself the answer rather than a reason to
    /// say nothing: see <c>TargetDrift.IsDrifted</c>, which counts this.
    /// </summary>
    [Fact]
    public void An_active_profile_with_no_manifest_is_a_folder_nothing_was_applied_to()
    {
        using var fixture = new DriftFixture();
        fixture.Folder.WriteFile("fs25_a.zip", "one");

        var report = fixture.Check();

        Assert.Equal(DriftStatus.NeverSynced, report.Status);

        // And nothing is claimed about the contents: the file in there is neither an addition nor a
        // difference, because there is no record saying what should have been.
        Assert.Equal(0, report.DifferenceCount);
    }

    /// <summary>
    /// <b>An intent recorded and work not done</b>, which is what an activation whose apply failed
    /// leaves behind: the manifest is written only on success, so the folder is still describing the
    /// list it was on while the game means to follow another. Folded into "nothing known" this was
    /// silent, and silent is the one thing it must not be - it is the normal end of an evening where
    /// the dedicated server was locked and the client applied fine.
    /// </summary>
    [Fact]
    public void A_manifest_for_a_different_profile_is_an_apply_that_did_not_land()
    {
        using var fixture = new DriftFixture();
        fixture.Sync("Old-school", ("fs25_a.zip", "one"));

        var report = fixture.Service.Check(
            fixture.Target,
            new ActiveProfile(_repoId, Guid.NewGuid()),
            fixture.Folder.Path);

        Assert.Equal(DriftStatus.NotApplied, report.Status);

        // Which list it is still on, so the notice can say it. The revision beside it is that
        // profile's and is deliberately not carried: two profiles' revisions are not comparable.
        Assert.Equal("Old-school", report.AppliedProfileName);
    }

    /// <summary>
    /// The settings having been repointed, which is not an apply that did not happen: the user did it
    /// a moment ago, and the remedy being the same does not make it the same sentence.
    /// </summary>
    [Fact]
    public void A_manifest_for_a_different_folder_is_the_settings_having_been_repointed()
    {
        using var fixture = new DriftFixture();
        using var elsewhere = new TempDirectory("drift-repointed");

        fixture.Sync(("fs25_a.zip", "one"));

        var report = fixture.Service.Check(
            fixture.Target,
            new ActiveProfile(_repoId, _profileId),
            elsewhere.Path);

        Assert.Equal(DriftStatus.FolderRepointed, report.Status);
    }

    /// <summary>
    /// And the repoint wins over the profile comparison, because it is the reason the other one
    /// cannot be made: the manifest is describing somewhere else entirely.
    /// </summary>
    [Fact]
    public void A_repointed_folder_is_not_reported_as_an_apply_that_did_not_land()
    {
        using var fixture = new DriftFixture();
        using var elsewhere = new TempDirectory("drift-repointed-other-profile");

        fixture.Sync(("fs25_a.zip", "one"));

        var report = fixture.Service.Check(
            fixture.Target,
            new ActiveProfile(_repoId, Guid.NewGuid()),
            elsewhere.Path);

        Assert.Equal(DriftStatus.FolderRepointed, report.Status);
    }

    [Fact]
    public void An_unreachable_folder_is_unknown_not_drifted()
    {
        using var fixture = new DriftFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        // An unplugged drive or an offline network path. Warning about mods that may be perfectly
        // fine is worse than saying nothing.
        var report = fixture.Service.Check(
            fixture.Target,
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
            fixture.Target,
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
            fixture.Target,
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
            fixture.Target,
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
            fixture.Manifests.TryRead(fixture.Target)!,
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
        public ModTargetRef Target { get; } = Keys.Target();


        /// <summary>Writes the files and the manifest that says they are what was installed.</summary>
        public void Sync(params (string Name, string Content)[] files)
            => Sync([.. files.Select(x => (x.Name, x.Content, false, (string?)null))]);

        /// <summary>The same, recording what the profile applied here was called.</summary>
        public void Sync(string profileName, params (string Name, string Content)[] files)
            => Sync([.. files.Select(x => (x.Name, x.Content, false, (string?)null))], profileName);

        public void Sync(params (string Name, string Content, bool Locked, string? DisplayName)[] files)
            => Sync(files, null);

        private void Sync((string Name, string Content, bool Locked, string? DisplayName)[] files, string? profileName)
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
                Target = Target,
                RepoId = _repoId,
                ProfileId = _profileId,
                ProfileName = profileName,
                SyncedAt = DateTimeOffset.UtcNow,
                ModFolder = Folder.Path,
                Entries = entries
            });
        }

        public DriftReport Check()
            => Service.Check(Target, new ActiveProfile(_repoId, _profileId), Folder.Path);

        public void Dispose()
        {
            Folder.Dispose();
            _manifests.Dispose();
        }
    }
}
