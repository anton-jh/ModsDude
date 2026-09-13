using ModsDude.Client.Core.GameAdapters;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Tests.Sync;

public class DriftMonitorTests
{
    private readonly static Guid _repoId = Guid.NewGuid();
    private readonly static Guid _profileId = Guid.NewGuid();


    [Fact]
    public void A_folder_the_game_changed_while_the_app_was_closed_is_found_at_startup()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        // The normal case: nothing was observing at the moment of the change. The manifest is what
        // makes a comparison made later still mean something.
        fixture.Folder.WriteFile("fs25_a.zip", "the game updated this");

        fixture.Monitor.Check();

        Assert.True(fixture.Monitor.HasDrift);
        Assert.True(fixture.Monitor.ShouldNotify);
    }

    /// <summary>
    /// The half of drift a directory listing can never find: the folder is exactly what was
    /// installed, and somebody has saved the profile since. Two integers are the whole mechanism,
    /// which is what lets the offline check say it.
    /// </summary>
    [Fact]
    public void A_profile_somebody_else_saved_is_drift_even_when_the_folder_is_untouched()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(6, ("fs25_a.zip", "one"));

        fixture.Revisions.Head = 8;
        fixture.Monitor.Check();

        var drift = Assert.Single(fixture.Monitor.Drifted);

        Assert.True(drift.Report.ProfileHasMoved);
        Assert.Equal(6, drift.Report.AppliedRevision);
        Assert.Equal(8, drift.Report.CurrentRevision);

        // Nothing in the folder differs, so a difference count would have said "0 differences".
        Assert.Equal(0, drift.Report.DifferenceCount);
    }

    [Fact]
    public void A_game_on_the_profiles_current_revision_is_not_drifted_by_that()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(8, ("fs25_a.zip", "one"));

        fixture.Revisions.Head = 8;
        fixture.Monitor.Check();

        Assert.False(fixture.Monitor.HasDrift);
    }

    /// <summary>
    /// A game holding a past savegame is behind head <em>by construction</em> - that savegame's
    /// revision does not move - so comparing it against head would report drift permanently, and offer
    /// a re-apply to head that the apply table refuses. The comparison is against the revision the
    /// savegame targets instead, and nothing is suppressed to achieve it: it comes out equal on its
    /// own.
    /// </summary>
    [Fact]
    public void A_game_holding_a_past_savegame_is_not_reported_as_behind_the_profile()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(4, ("fs25_a.zip", "one"));

        fixture.Revisions.Head = 1004;
        fixture.Held.Hold(fixture.Candidates.Game, _profileId, targetRevision: 4);
        fixture.Monitor.Check();

        Assert.False(fixture.Monitor.HasDrift);
    }

    /// <summary>
    /// Folder drift under a past savegame still reports, and against that savegame's revision - the
    /// re-apply it offers has to target the list the savegame needs rather than head.
    /// </summary>
    [Fact]
    public void Folder_drift_under_a_past_savegame_still_reports_against_its_revision()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(4, ("fs25_a.zip", "one"));

        fixture.Revisions.Head = 1004;
        fixture.Held.Hold(fixture.Candidates.Game, _profileId, targetRevision: 4);
        fixture.Folder.WriteFile("fs25_b.zip", "two");
        fixture.Monitor.Check();

        var drift = Assert.Single(fixture.Monitor.Drifted);

        Assert.Equal(["fs25_b.zip"], drift.Report.Added);
        Assert.False(drift.Report.ProfileHasMoved);
        Assert.Equal(4, drift.Report.CurrentRevision);
    }

    /// <summary>
    /// The client holds one repo's profiles at a time, so the head is unknown for every other repo -
    /// and unknown has to read as "not asked" rather than as "unchanged" or as drift.
    /// </summary>
    [Fact]
    public void A_profile_whose_revision_this_client_does_not_know_is_not_reported_as_moved()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(6, ("fs25_a.zip", "one"));

        fixture.Monitor.Check();

        Assert.False(fixture.Monitor.HasDrift);
    }

    /// <summary>
    /// A manifest written before profiles had revisions records none. That is "not recorded", which
    /// says nothing about the folder - and must not turn every pre-existing game into drift on
    /// the first launch after the upgrade.
    /// </summary>
    [Fact]
    public void A_manifest_from_before_revisions_is_not_reported_as_moved()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(null, ("fs25_a.zip", "one"));

        fixture.Revisions.Head = 8;
        fixture.Monitor.Check();

        Assert.False(fixture.Monitor.HasDrift);
    }

    [Fact]
    public void A_dismissed_notice_comes_back_when_the_profile_moves_again()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(6, ("fs25_a.zip", "one"));

        fixture.Revisions.Head = 7;
        fixture.Monitor.Check();
        fixture.Monitor.Dismiss();

        Assert.False(fixture.Monitor.ShouldNotify);

        fixture.Revisions.Head = 8;
        fixture.Monitor.Check();

        Assert.True(fixture.Monitor.ShouldNotify);
    }

    [Fact]
    public void Activation_checks_are_throttled_so_alt_tabbing_costs_one_listing()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        Assert.True(fixture.Monitor.Check(DriftCheckReason.WindowActivated));

        fixture.Time.Advance(DriftMonitor.ThrottleWindow - TimeSpan.FromSeconds(1));

        Assert.False(fixture.Monitor.Check(DriftCheckReason.WindowActivated));
        Assert.False(fixture.Monitor.Check(DriftCheckReason.FolderChanged));
    }

    [Fact]
    public void The_throttle_lets_go_once_the_window_has_passed()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        fixture.Monitor.Check(DriftCheckReason.WindowActivated);
        fixture.Time.Advance(DriftMonitor.ThrottleWindow);

        Assert.True(fixture.Monitor.Check(DriftCheckReason.WindowActivated));
    }

    [Fact]
    public void A_check_the_user_asked_for_is_never_throttled()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        fixture.Monitor.Check(DriftCheckReason.WindowActivated);

        Assert.True(fixture.Monitor.Check());
        Assert.True(fixture.Monitor.Check());
    }

    [Fact]
    public void Dismissal_lasts_while_the_drift_set_is_the_same()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));
        fixture.Folder.WriteFile("fs25_a.zip", "the game updated this");

        fixture.Monitor.Check();
        fixture.Monitor.Dismiss();

        Assert.True(fixture.Monitor.IsDismissed);
        Assert.False(fixture.Monitor.ShouldNotify);

        // Re-checking the same problem does not resurrect the notice.
        fixture.Monitor.Check();

        Assert.False(fixture.Monitor.ShouldNotify);
        Assert.True(fixture.Monitor.HasDrift);
    }

    [Fact]
    public void Dismissal_ends_the_moment_the_drift_set_changes()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"), ("fs25_b.zip", "two"));
        fixture.Folder.WriteFile("fs25_a.zip", "the game updated this");

        fixture.Monitor.Check();
        fixture.Monitor.Dismiss();

        Assert.False(fixture.Monitor.ShouldNotify);

        // A second mod going wrong is a different problem from the one that was waved away.
        fixture.Folder.WriteFile("fs25_b.zip", "and this");
        fixture.Monitor.Check();

        Assert.True(fixture.Monitor.ShouldNotify);
    }

    [Fact]
    public void Dismissal_does_not_survive_a_restart()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));
        fixture.Folder.WriteFile("fs25_a.zip", "the game updated this");

        fixture.Monitor.Check();
        fixture.Monitor.Dismiss();

        // Nothing about dismissal is persisted: a dismissed warning that never comes back is a
        // savegame silently at risk.
        using var restarted = fixture.Restart();
        restarted.Check();

        Assert.True(restarted.ShouldNotify);
    }

    [Fact]
    public void Drift_going_away_takes_the_notice_with_it()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));
        fixture.Folder.WriteFile("fs25_a.zip", "the game updated this");

        fixture.Monitor.Check();

        Assert.True(fixture.Monitor.ShouldNotify);

        // What a re-apply leaves behind.
        fixture.Sync(("fs25_a.zip", "one"));
        fixture.Monitor.Check();

        Assert.False(fixture.Monitor.HasDrift);
        Assert.False(fixture.Monitor.ShouldNotify);
    }

    [Fact]
    public void An_unreachable_folder_is_not_reported_as_drift()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        // An unplugged drive. Unknown, not drifted - warning about mods that may be perfectly fine
        // is worse than saying nothing.
        fixture.Candidates.ModFolder = fixture.Folder.Combine("gone");

        fixture.Monitor.Check();

        Assert.False(fixture.Monitor.HasDrift);
    }

    [Fact]
    public void A_game_with_no_active_profile_is_not_checked_at_all()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));
        fixture.Folder.WriteFile("fs25_a.zip", "the game updated this");

        fixture.Candidates.ActiveProfile = null;

        fixture.Monitor.Check();

        Assert.False(fixture.Monitor.HasDrift);
    }

    /// <summary>
    /// The normal BeamMP evening, and the reason the whole phase exists: the dedicated server was
    /// locked while the client applied fine, so one folder is on the old mod list and the other is
    /// not. The check has to say which - a per-game answer could only say "something differs".
    /// </summary>
    [Fact]
    public void One_folder_drifting_is_reported_against_that_folder_and_not_the_other()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));
        fixture.SyncSecondTarget(("fs25_a.zip", "one"));

        fixture.SecondFolder.WriteFile("fs25_a.zip", "the game updated this");
        fixture.Monitor.Check();

        var drift = Assert.Single(fixture.Monitor.Drifted);

        Assert.Equal(fixture.Candidates.SecondTarget, drift.Target?.Target);
        Assert.Equal(["fs25_a.zip"], drift.Report.Changed);
    }

    /// <summary>
    /// And a folder that is exactly what was applied to it stays quiet, which is what makes the
    /// answer above worth reading: both folders are checked, and only one of them is news.
    /// </summary>
    [Fact]
    public void Both_folders_are_checked_and_a_matching_one_says_nothing()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));
        fixture.SyncSecondTarget(("fs25_a.zip", "one"));

        fixture.Monitor.Check();

        Assert.False(fixture.Monitor.HasDrift);

        // Both, not just the first: an unapplied second folder used to be invisible because the
        // manifest was the game's and the first folder's answer was the game's answer.
        fixture.Folder.WriteFile("fs25_b.zip", "two");
        fixture.SecondFolder.WriteFile("fs25_b.zip", "two");
        fixture.Monitor.Check();

        Assert.Equal(2, fixture.Monitor.Drifted.Count);
    }

    /// <summary>
    /// Dismissal is per drift set, and the second folder going wrong under a notice waved away about
    /// the first is a different problem - which the signature has to be keyed finely enough to see.
    /// </summary>
    [Fact]
    public void A_dismissed_notice_comes_back_when_the_other_folder_goes_wrong()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));
        fixture.SyncSecondTarget(("fs25_a.zip", "one"));

        fixture.Folder.WriteFile("fs25_a.zip", "the game updated this");
        fixture.Monitor.Check();
        fixture.Monitor.Dismiss();

        Assert.False(fixture.Monitor.ShouldNotify);

        fixture.SecondFolder.WriteFile("fs25_a.zip", "and this");
        fixture.Monitor.Check();

        Assert.True(fixture.Monitor.ShouldNotify);
    }

    [Fact]
    public void The_drifted_game_carries_the_profile_name_the_manifest_recorded()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));
        fixture.Folder.WriteFile("fs25_a.zip", "the game updated this");

        fixture.Monitor.Check();

        // Recorded rather than looked up, so the notice can name it at startup and offline.
        Assert.Equal("Season 4", fixture.Monitor.Drifted.Single().ProfileName);
    }


    /// <summary>
    /// <b>The silence this slice exists to remove.</b> An activation whose apply failed leaves the
    /// manifest describing the profile the folder is still on - the manifest is written only on
    /// success - and that used to fold into "nothing known" and never reach the notice. One intent
    /// and several folders makes it ordinary: the dedicated server locked while the client applied
    /// fine is the normal BeamMP evening.
    /// </summary>
    [Fact]
    public void An_activation_that_did_not_land_reaches_the_notice()
    {
        using var fixture = new MonitorFixture();

        // Applied on Old-school, and then somebody activated Season 4 and the apply failed.
        fixture.Sync(("fs25_a.zip", "one"));
        fixture.Candidates.ActiveProfile = new ActiveProfile(_repoId, Guid.NewGuid());

        fixture.Monitor.Check();

        var drifted = Assert.Single(fixture.Monitor.Drifted);

        Assert.Equal(DriftStatus.NotApplied, drifted.Report.Status);
        Assert.True(fixture.Monitor.ShouldNotify);
    }

    /// <summary>
    /// And no manifest at all stays quiet, which is the other half of the split. Absent covers more
    /// than never-applied - a manifest that cannot be read, or one an older format wrote, arrives
    /// here the same way - so reporting it would fire on every game at once for a reason that is not
    /// about any of them.
    /// </summary>
    [Fact]
    public void A_folder_with_no_manifest_at_all_stays_quiet()
    {
        using var fixture = new MonitorFixture();
        fixture.Folder.WriteFile("fs25_a.zip", "one");

        fixture.Monitor.Check();

        Assert.Empty(fixture.Monitor.Drifted);
        Assert.False(fixture.Monitor.ShouldNotify);
    }

    /// <summary>
    /// <b>A held save belongs to the folder it is in.</b> A game reaching two of them holds its saves
    /// in particular ones, and an entry about the dedicated server saying "your savegame has moved"
    /// about an evening played on the MP client is the same conflation this phase exists to remove.
    /// </summary>
    [Fact]
    public void Savegame_drift_is_reported_on_the_folder_it_happened_in()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));
        fixture.SyncSecondTarget(("fs25_a.zip", "one"));

        fixture.Held.Drifted(Keys.Slot("savegame1", "server"));

        fixture.Monitor.Check();

        var drifted = Assert.Single(fixture.Monitor.Drifted);

        Assert.Equal(fixture.Candidates.SecondTarget, drifted.Target?.Target);
        Assert.Single(drifted.Report.SavegameDrift);
    }

    /// <summary>
    /// A save held in a folder with no mods beside it - the MP client whose saves live where its mods
    /// do not - belongs to no folder entry at all, because there is nothing there to compare. It is
    /// still worth saying, so it gets an entry about the game rather than being dropped for having
    /// nowhere to sit.
    /// </summary>
    [Fact]
    public void A_save_held_in_a_target_with_no_mod_folder_is_reported_about_the_game()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        fixture.Held.Drifted(Keys.Slot("savegame1", "saves-only"));

        fixture.Monitor.Check();

        var drifted = Assert.Single(fixture.Monitor.Drifted);

        Assert.Null(drifted.Target);
        Assert.Single(drifted.Report.SavegameDrift);
    }


    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }


    private sealed class FakeCandidates : IDriftCandidateSource
    {
        public ModTargetRef Target { get; } = Keys.Target();

        /// <summary>A second folder of the same game. Null unless a test is about having two.</summary>
        public ModTargetRef SecondTarget { get; } = Keys.Target("server");

        public GameIdentity Game => Target.Game;

        /// <summary>Null is a game whose settings point at no folder, which reaches no target.</summary>
        public string? ModFolder { get; set; }

        public string? SecondModFolder { get; set; }

        public ActiveProfile? ActiveProfile { get; set; } = new(_repoId, _profileId);

        public IReadOnlyList<DriftCandidate> GetDriftCandidates()
            => [new DriftCandidate(Game, "Farming Simulator 25", [.. Targets()], ActiveProfile)];


        private IEnumerable<GameModFolder> Targets()
        {
            if (ModFolder is string folder)
            {
                yield return new GameModFolder(Target, folder);
            }

            if (SecondModFolder is string second)
            {
                yield return new GameModFolder(SecondTarget, second);
            }
        }
    }


    /// <summary>
    /// What the client happens to know, which for a repo it has not loaded is nothing. Null is the
    /// default here for the same reason it is the default in the app.
    /// </summary>
    private sealed class FakeProfileRevisions : IProfileRevisions
    {
        public int? Head { get; set; }

        public int? GetHeadRevision(ActiveProfile profile) => Head;
    }


    /// <summary>
    /// A blob the game wrote through survives the check that can no longer see it.
    /// </summary>
    /// <remarks>
    /// The subtle half of the design. Finding a rewritten blob deletes it - leaving it would go on
    /// serving wrong bytes to every repo on the volume - so the very next check has no evidence left
    /// and reports nothing. If the notice were rebuilt from the latest check alone, the one warning
    /// that says the updater writes through hardlinks would vanish on the next alt-tab.
    /// </remarks>
    [Fact]
    public void A_rewritten_blob_stays_reported_after_the_evidence_is_gone()
    {
        using var fixture = new MonitorFixture(withStore: true);
        var hash = fixture.SyncLinked("fs25_a.zip", "one");

        // Written through the mod folder's name, which under hardlinking is the blob itself.
        fixture.Folder.WriteFile("fs25_a.zip", "the game wrote straight through the link");

        fixture.Monitor.Check();

        var found = Assert.Single(fixture.Monitor.StoreCorruption);

        Assert.Equal(hash, found.Hash);
        Assert.True(found.Removed);
        Assert.False(fixture.Store!.Contains(hash));

        // The second check finds nothing - there is nothing left to find - and the monitor still
        // says it happened.
        fixture.Monitor.Check();

        Assert.Empty(fixture.Monitor.Drifted.Single().Report.StoreCorruption);
        Assert.Single(fixture.Monitor.StoreCorruption);
        Assert.True(fixture.Monitor.HasStoreCorruption);
    }

    [Fact]
    public void An_in_game_update_that_renames_over_leaves_the_store_uncorrupted()
    {
        using var fixture = new MonitorFixture(withStore: true);
        var hash = fixture.SyncLinked("fs25_a.zip", "one");

        // What Farming Simulator does: a new file, moved onto the old name. The link breaks, the
        // blob keeps its bytes.
        var staged = fixture.Folder.WriteFile("staged.tmp", "a newer build");
        File.Move(staged, fixture.Folder.Combine("fs25_a.zip"), overwrite: true);

        fixture.Monitor.Check();

        // Drift, because the folder changed. No corruption, because the store was never written to.
        Assert.True(fixture.Monitor.HasDrift);
        Assert.False(fixture.Monitor.HasStoreCorruption);
        Assert.True(fixture.Store!.Contains(hash));
    }

    /// <summary>
    /// Dismissing waves away what is on screen. A second blob going wrong is not that.
    /// </summary>
    [Fact]
    public void A_second_rewritten_blob_brings_a_dismissed_notice_back()
    {
        using var fixture = new MonitorFixture(withStore: true);
        fixture.SyncLinked("fs25_a.zip", "one");

        fixture.Folder.WriteFile("fs25_a.zip", "written through");
        fixture.Monitor.Check();

        fixture.Monitor.Dismiss();

        Assert.True(fixture.Monitor.IsDismissed);
        Assert.False(fixture.Monitor.ShouldNotify);

        // A different mod, going the same way.
        fixture.SyncLinked("fs25_b.zip", "two");
        fixture.Folder.WriteFile("fs25_b.zip", "written through as well");
        fixture.Monitor.Check();

        Assert.Equal(2, fixture.Monitor.StoreCorruption.Count);
        Assert.False(fixture.Monitor.IsDismissed);
        Assert.True(fixture.Monitor.ShouldNotify);
    }


    private sealed class MonitorFixture : IDisposable
    {
        private const long _oneGigabyte = 1024L * 1024 * 1024;

        private readonly TempDirectory _manifests = new("monitor-manifests");
        private readonly TempDirectory _storeRoot = new("monitor-store");


        /// <param name="withStore">
        /// Whether this fixture's disk is served by its own store, and therefore whether the
        /// rewritten-blob check has anything to look at. Off by default: it is the only thing here
        /// that needs a real store on a real volume.
        /// </param>
        public MonitorFixture(bool withStore = false)
        {
            Folder = new TempDirectory("monitor-mods");
            Candidates = new FakeCandidates { ModFolder = Folder.Path };
            Manifests = new SyncManifestStore(_manifests.Path);
            Drift = new DriftService(Manifests, NullLogger<DriftService>.Instance);

            if (withStore)
            {
                Store = new ContentStore("C:\\", _storeRoot.Path, _oneGigabyte);

                Integrity = new StoreIntegrityService(
                    new FakeStoreProvider(Store),
                    Manifests,
                    NullLogger<StoreIntegrityService>.Instance);
            }

            Held = new FakeHeldSavegames(Manifests);

            Monitor = new DriftMonitor(Candidates, Drift, Manifests, Revisions, Time, Held, Integrity);
        }


        public TempDirectory Folder { get; }
        public FakeCandidates Candidates { get; }

        /// <summary>The game's other folder, used only by the tests that give it one.</summary>
        public TempDirectory SecondFolder { get; } = new("monitor-second-mods");
        public SyncManifestStore Manifests { get; }
        public DriftService Drift { get; }
        public TestTimeProvider Time { get; } = new();

        /// <summary>Null unless this fixture was built with one - see the constructor.</summary>
        public ContentStore? Store { get; }
        public StoreIntegrityService? Integrity { get; }

        /// <summary>Answers nothing by default, which is the state before any repo has been loaded.</summary>
        public FakeProfileRevisions Revisions { get; } = new();

        /// <summary>Holding nothing by default, which is nearly every game nearly all the time.</summary>
        public FakeHeldSavegames Held { get; }

        public DriftMonitor Monitor { get; }


        /// <summary>Writes the files and the manifest that says they are what was installed.</summary>
        public void Sync(params (string Name, string Content)[] files)
            => Sync(null, files);

        /// <summary>
        /// Gives this game a second folder and syncs it, as a dedicated server beside an MP client.
        /// </summary>
        public void SyncSecondTarget(params (string Name, string Content)[] files)
        {
            Candidates.SecondModFolder = SecondFolder.Path;

            var entries = new List<SyncManifestEntry>();

            foreach (var (name, content) in files)
            {
                var info = new FileInfo(SecondFolder.WriteFile(name, content));

                entries.Add(Entry(name, content, info));
            }

            Manifests.Write(new SyncManifest
            {
                Target = Candidates.SecondTarget,
                RepoId = _repoId,
                ProfileId = _profileId,
                ProfileName = "Season 4",
                SyncedAt = DateTimeOffset.UtcNow,
                ModFolder = SecondFolder.Path,
                Entries = entries
            });
        }

        /// <param name="revision">Which revision of the profile the manifest records as applied.</param>
        public void Sync(int? revision, params (string Name, string Content)[] files)
        {
            var entries = new List<SyncManifestEntry>();

            foreach (var (name, content) in files)
            {
                var path = Folder.WriteFile(name, content);
                var info = new FileInfo(path);

                entries.Add(Entry(name, content, info));
            }

            WriteManifest(entries, revision);
        }

        /// <summary>
        /// Installs a mod the way a disk served by its own store does: one file on disk, named both
        /// from the store and from the mod folder.
        /// </summary>
        /// <returns>The address the blob is filed under.</returns>
        public string SyncLinked(string name, string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = ModContentHasher.Format(SHA256.HashData(bytes));

            Store!.IngestAsync(new MemoryStream(bytes), hash, null, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var path = Folder.Combine(name);

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            Assert.True(FileLinks.TryCreateHardLink(path, Store.GetBlobPath(hash)), "the test needs a real hardlink");

            WriteManifest([Entry(name, content, new FileInfo(path))], null);

            return hash;
        }

        /// <summary>A second monitor over the same state - what the next launch has.</summary>
        public DriftMonitor Restart()
            => new(Candidates, Drift, Manifests, Revisions, Time, storeIntegrity: Integrity);

        public void Dispose()
        {
            Monitor.Dispose();
            Folder.Dispose();
            SecondFolder.Dispose();
            _manifests.Dispose();
            _storeRoot.Dispose();
        }


        private static SyncManifestEntry Entry(string name, string content, FileInfo info) => new(
            Path.GetFileNameWithoutExtension(name),
            "1.0.0",
            ModContentHasher.Format(SHA256.HashData(Encoding.UTF8.GetBytes(content))),
            name,
            info.Length,
            info.LastWriteTimeUtc);

        private void WriteManifest(IReadOnlyList<SyncManifestEntry> entries, int? revision)
        {
            Manifests.Write(new SyncManifest
            {
                Target = Candidates.Target,
                RepoId = _repoId,
                ProfileId = _profileId,
                ProfileName = "Season 4",
                ProfileRevision = revision,
                SyncedAt = DateTimeOffset.UtcNow,
                ModFolder = Folder.Path,
                Entries = entries
            });
        }
    }
}
