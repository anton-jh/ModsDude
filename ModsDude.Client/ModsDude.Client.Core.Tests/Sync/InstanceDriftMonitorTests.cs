using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Tests.Sync;

public class InstanceDriftMonitorTests
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
    public void An_instance_on_the_profiles_current_revision_is_not_drifted_by_that()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(8, ("fs25_a.zip", "one"));

        fixture.Revisions.Head = 8;
        fixture.Monitor.Check();

        Assert.False(fixture.Monitor.HasDrift);
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
    /// says nothing about the folder - and must not turn every pre-existing instance into drift on
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

        fixture.Time.Advance(InstanceDriftMonitor.ThrottleWindow - TimeSpan.FromSeconds(1));

        Assert.False(fixture.Monitor.Check(DriftCheckReason.WindowActivated));
        Assert.False(fixture.Monitor.Check(DriftCheckReason.FolderChanged));
    }

    [Fact]
    public void The_throttle_lets_go_once_the_window_has_passed()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));

        fixture.Monitor.Check(DriftCheckReason.WindowActivated);
        fixture.Time.Advance(InstanceDriftMonitor.ThrottleWindow);

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
    public void An_instance_with_no_active_profile_is_not_checked_at_all()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));
        fixture.Folder.WriteFile("fs25_a.zip", "the game updated this");

        fixture.Candidates.ActiveProfile = null;

        fixture.Monitor.Check();

        Assert.False(fixture.Monitor.HasDrift);
    }

    [Fact]
    public void The_drifted_instance_carries_the_profile_name_the_manifest_recorded()
    {
        using var fixture = new MonitorFixture();
        fixture.Sync(("fs25_a.zip", "one"));
        fixture.Folder.WriteFile("fs25_a.zip", "the game updated this");

        fixture.Monitor.Check();

        // Recorded rather than looked up, so the notice can name it at startup and offline.
        Assert.Equal("Season 4", fixture.Monitor.Drifted.Single().ProfileName);
    }


    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }


    private sealed class FakeCandidates : IDriftCandidateSource
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
        public string? ModFolder { get; set; }
        public ActiveProfile? ActiveProfile { get; set; } = new(_repoId, _profileId);

        public IReadOnlyList<DriftCandidate> GetDriftCandidates()
            => [new DriftCandidate(InstanceId, "Farming Simulator 25", ModFolder, ActiveProfile)];
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


    private sealed class MonitorFixture : IDisposable
    {
        private readonly TempDirectory _manifests = new("monitor-manifests");


        public MonitorFixture()
        {
            Folder = new TempDirectory("monitor-mods");
            Candidates = new FakeCandidates { ModFolder = Folder.Path };
            Manifests = new SyncManifestStore(_manifests.Path);
            Drift = new InstanceDriftService(Manifests, NullLogger<InstanceDriftService>.Instance);
            Monitor = new InstanceDriftMonitor(Candidates, Drift, Manifests, Revisions, Time);
        }


        public TempDirectory Folder { get; }
        public FakeCandidates Candidates { get; }
        public SyncManifestStore Manifests { get; }
        public InstanceDriftService Drift { get; }
        public TestTimeProvider Time { get; } = new();

        /// <summary>Answers nothing by default, which is the state before any repo has been loaded.</summary>
        public FakeProfileRevisions Revisions { get; } = new();

        public InstanceDriftMonitor Monitor { get; }


        /// <summary>Writes the files and the manifest that says they are what was installed.</summary>
        public void Sync(params (string Name, string Content)[] files)
            => Sync(null, files);

        /// <param name="revision">Which revision of the profile the manifest records as applied.</param>
        public void Sync(int? revision, params (string Name, string Content)[] files)
        {
            var entries = new List<SyncManifestEntry>();

            foreach (var (name, content) in files)
            {
                var path = Folder.WriteFile(name, content);
                var info = new FileInfo(path);

                entries.Add(new SyncManifestEntry(
                    Path.GetFileNameWithoutExtension(name),
                    "1.0.0",
                    ModContentHasher.Format(SHA256.HashData(Encoding.UTF8.GetBytes(content))),
                    name,
                    info.Length,
                    info.LastWriteTimeUtc));
            }

            Manifests.Write(new SyncManifest
            {
                InstanceId = Candidates.InstanceId,
                RepoId = _repoId,
                ProfileId = _profileId,
                ProfileName = "Season 4",
                ProfileRevision = revision,
                SyncedAt = DateTimeOffset.UtcNow,
                ModFolder = Folder.Path,
                Entries = entries
            });
        }

        /// <summary>A second monitor over the same state - what the next launch has.</summary>
        public InstanceDriftMonitor Restart()
            => new(Candidates, Drift, Manifests, Revisions, Time);

        public void Dispose()
        {
            Monitor.Dispose();
            Folder.Dispose();
            _manifests.Dispose();
        }
    }
}
