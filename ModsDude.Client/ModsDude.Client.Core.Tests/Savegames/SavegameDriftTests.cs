using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>
/// The three drift states, both as the pure rule and as the check that runs on a real disk.
/// </summary>
public class SavegameDriftTests
{
    private static readonly SavegameSlotId _slot = new("savegame1");

    private static readonly Guid _repoId = Guid.NewGuid();
    private static readonly Guid _savegameId = Guid.NewGuid();
    private static readonly Guid _profileId = Guid.NewGuid();


    [Fact]
    public void A_slot_that_still_holds_what_was_written_into_it_has_not_drifted()
    {
        var kinds = SavegameDriftRules.Classify(Binding(), "aaaa", headVersion: 4, _profileId, appliedRevision: 6);

        Assert.Empty(kinds);
    }

    /// <summary>
    /// Somebody's evening, and it exists nowhere else until it is checked in.
    /// </summary>
    [Fact]
    public void A_slot_whose_contents_have_moved_is_unchecked_in_play()
    {
        var kinds = SavegameDriftRules.Classify(Binding(), "bbbb", headVersion: 4, _profileId, appliedRevision: 6);

        Assert.Equal([SavegameDriftKind.UncheckedInPlay], kinds);
    }

    /// <summary>
    /// The hash is recorded lowercase by one part of the client and could be read back by another. A
    /// casing difference reporting an evening that is not there is the false alarm that teaches people
    /// to click the notice away.
    /// </summary>
    [Fact]
    public void Hash_casing_is_not_a_difference()
    {
        Assert.Empty(SavegameDriftRules.Classify(Binding(hash: "AAAA"), "aaaa", null, null, null));
    }

    /// <summary>
    /// A hash nobody computed says nothing. The opposite answer to the one the slot safety check
    /// gives for the same unknown, and deliberately: that one is deciding whether to destroy an
    /// evening, this one whether to raise a warning.
    /// </summary>
    [Fact]
    public void An_unhashed_slot_reports_nothing_rather_than_reporting_play()
    {
        Assert.Empty(SavegameDriftRules.Classify(Binding(), null, null, null, null));
    }

    [Fact]
    public void A_head_past_the_version_being_held_is_a_takeover()
    {
        var kinds = SavegameDriftRules.Classify(Binding(version: 4), "aaaa", headVersion: 5, _profileId, appliedRevision: 6);

        Assert.Equal([SavegameDriftKind.TakenOverAndCheckedIn], kinds);
    }

    /// <summary>
    /// A client holding a head number older than the binding has not refreshed; inventing a takeover
    /// out of that would fire the notice on stale data rather than on anything that happened.
    /// </summary>
    [Fact]
    public void A_head_at_or_behind_the_held_version_is_not_a_takeover()
    {
        Assert.Empty(SavegameDriftRules.Classify(Binding(version: 4), "aaaa", headVersion: 4, _profileId, 6));
        Assert.Empty(SavegameDriftRules.Classify(Binding(version: 4), "aaaa", headVersion: 3, _profileId, 6));
    }

    /// <summary>
    /// The case that corrupts saves, and the reason the locking exists at all - and it costs no I/O
    /// whatsoever, because both numbers are already in local state.
    /// </summary>
    [Fact]
    public void A_folder_on_another_revision_than_the_save_was_checked_out_on_is_drift()
    {
        var kinds = SavegameDriftRules.Classify(Binding(revision: 6), "aaaa", headVersion: 4, _profileId, appliedRevision: 8);

        Assert.Equal([SavegameDriftKind.PlayedOnAnotherModList], kinds);
    }

    /// <summary>
    /// Two revision numbers of two different profiles are not comparable at all - revision 6 of one
    /// list and revision 6 of another are different mod lists that happen to share an integer.
    /// </summary>
    [Fact]
    public void A_folder_applied_to_a_different_profile_is_drift_whatever_the_numbers_say()
    {
        var kinds = SavegameDriftRules.Classify(Binding(revision: 6), "aaaa", null, Guid.NewGuid(), appliedRevision: 6);

        Assert.Equal([SavegameDriftKind.PlayedOnAnotherModList], kinds);
    }

    [Fact]
    public void A_binding_with_no_recorded_revision_leaves_the_question_unasked()
    {
        var binding = Binding() with { ProfileId = null, ProfileRevision = null };

        Assert.Empty(SavegameDriftRules.Classify(binding, "aaaa", null, _profileId, appliedRevision: 8));
    }

    /// <summary>
    /// The worst case, and the one where saying only half of it would be actively misleading: an
    /// evening in the slot that the server will refuse, because somebody else already checked in.
    /// </summary>
    [Fact]
    public void Play_and_a_takeover_are_both_reported()
    {
        var kinds = SavegameDriftRules.Classify(Binding(version: 4), "bbbb", headVersion: 5, _profileId, appliedRevision: 6);

        Assert.Equal(
            [SavegameDriftKind.UncheckedInPlay, SavegameDriftKind.TakenOverAndCheckedIn],
            kinds);
    }


    [Fact]
    public async Task An_instance_holding_nothing_reports_nothing()
    {
        using var harness = new DriftHarness();

        Assert.Empty(await harness.Service.CheckDriftAsync(harness.Instance.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Play_in_a_held_slot_is_found_by_hashing_it()
    {
        using var harness = new DriftHarness();

        harness.Hold(await harness.WriteAndHashAsync("a farm"));
        harness.WriteSlotFile("a farm, and a barn");

        var drift = Assert.Single(await harness.Service.CheckDriftAsync(harness.Instance.Id, CancellationToken.None));

        Assert.Equal(SavegameDriftKind.UncheckedInPlay, drift.Kind);
        Assert.Equal(_slot, drift.Slot);
        Assert.Equal(_savegameId, drift.SavegameId);
    }

    [Fact]
    public async Task An_untouched_held_slot_reports_nothing()
    {
        using var harness = new DriftHarness();

        harness.Hold(await harness.WriteAndHashAsync("a farm"));

        Assert.Empty(await harness.Service.CheckDriftAsync(harness.Instance.Id, CancellationToken.None));
    }

    [Fact]
    public async Task A_head_the_client_knows_has_moved_past_the_binding_is_reported()
    {
        using var harness = new DriftHarness();

        harness.Hold(await harness.WriteAndHashAsync("a farm"), version: 3);
        harness.Heads.Set(_savegameId, 4);

        var drift = Assert.Single(await harness.Service.CheckDriftAsync(harness.Instance.Id, CancellationToken.None));

        Assert.Equal(SavegameDriftKind.TakenOverAndCheckedIn, drift.Kind);
        Assert.Equal(3, drift.HeldVersion);
        Assert.Equal(4, drift.HeadVersion);
    }

    /// <summary>
    /// No hashing, no network, no directory listing beyond the slot list: two integers already in
    /// local state. It is the reason this state is worth having at all.
    /// </summary>
    [Fact]
    public async Task A_folder_re_synced_onto_another_revision_is_reported()
    {
        using var harness = new DriftHarness();

        harness.Hold(await harness.WriteAndHashAsync("a farm"), revision: 6);
        harness.WriteManifest(revision: 8);

        var drift = Assert.Single(await harness.Service.CheckDriftAsync(harness.Instance.Id, CancellationToken.None));

        Assert.Equal(SavegameDriftKind.PlayedOnAnotherModList, drift.Kind);
        Assert.Equal(6, drift.PlayedRevision);
        Assert.Equal(8, drift.AppliedRevision);
    }

    /// <summary>
    /// The binding outlived what it described - somebody deleted the save from inside the game. There
    /// is nothing there to have been played, and hashing a missing folder would report the empty
    /// archive as an evening.
    /// </summary>
    [Fact]
    public async Task A_held_slot_whose_folder_is_gone_reports_no_play()
    {
        using var harness = new DriftHarness();

        harness.Hold(await harness.WriteAndHashAsync("a farm"));

        Directory.Delete(harness.SlotPath, recursive: true);

        Assert.Empty(await harness.Service.CheckDriftAsync(harness.Instance.Id, CancellationToken.None));
    }

    /// <summary>
    /// Savegame drift reaches the notice on the same report as mod drift, so that one sentence can
    /// carry both halves of "this folder is not what you think it is".
    /// </summary>
    [Fact]
    public void Savegame_drift_rides_on_the_instance_drift_report()
    {
        using var manifests = new TempDirectory("savegame-drift-report");
        using var modFolder = new TempDirectory("savegame-drift-mods");

        var service = new InstanceDriftService(new SyncManifestStore(manifests.Path), NullLogger<InstanceDriftService>.Instance);
        var drift = new[] { new SavegameDrift(_repoId, _savegameId, _slot, SavegameDriftKind.UncheckedInPlay) };

        var report = service.Check(
            Guid.NewGuid(),
            new ActiveProfile(_repoId, _profileId),
            modFolder.Path,
            savegameDrift: drift);

        // Never synced, so the mod half has nothing to say - and the savegame half is carried anyway.
        Assert.Equal(InstanceDriftStatus.NeverSynced, report.Status);
        Assert.True(report.HasSavegameDrift);
        Assert.Equal(SavegameDriftKind.UncheckedInPlay, Assert.Single(report.SavegameDrift).Kind);

        // And it is enough on its own to make the notice fire, without pretending the mod folder
        // drifted.
        Assert.True(new InstanceDrift(new DriftCandidate(Guid.NewGuid(), "FS25", modFolder.Path, null), report, null).IsDrifted);
    }


    private static SavegameCheckoutBinding Binding(int version = 4, string hash = "aaaa", int? revision = 6) => new(
        _repoId,
        _savegameId,
        _slot.Value,
        version,
        hash,
        DateTime.UtcNow)
    {
        ProfileId = _profileId,
        ProfileRevision = revision
    };


    /// <summary>The drift check over a real slot folder, a real packer and real local state.</summary>
    private sealed class DriftHarness : IDisposable
    {
        private readonly TempDirectory _slots = new("savegame-drift-slots");
        private readonly TempDirectory _manifests = new("savegame-drift-manifests");

        private readonly SavegameBindingStore _bindings;
        private readonly SyncManifestStore _manifestStore;


        public DriftHarness()
        {
            var persisted = new PersistedLocalInstance
            {
                Id = Guid.NewGuid(),
                Scope = new InstanceScope("farmingSimulator", "fs25"),
                GameAdapterId = new GameAdapterId("farmingSimulator", 1),
                Name = "Farming Simulator 25",
                AdapterInstanceSettings = "{}",
                ModFolder = _slots.Path,
                ActiveProfile = new ActiveProfile(_repoId, _profileId)
            };

            State.Add(persisted);
            Instance = new LocalInstance(persisted);

            Adapter = new FakeSavegameAdapter(_slots.Path, _slot.Value);
            _bindings = new SavegameBindingStore(State);
            _manifestStore = new SyncManifestStore(_manifests.Path);

            WriteManifest(revision: 6);

            Service = new SavegameService(
                Server,
                Server,
                new SavegamePacker(),
                _bindings,
                new FakeInstanceSavegameAdapters(Adapter),
                new FakeSavegameDownloader(Server),
                new FakeSavegameUploader(Server),
                _manifestStore,
                new FakeSlotRecycleBin(),
                NullLogger<SavegameService>.Instance,
                Heads);
        }


        public FakeSavegameServer Server { get; } = new();
        public FakeSavegameHeadVersions Heads { get; } = new();
        public FakeInstanceState State { get; } = new();
        public FakeSavegameAdapter Adapter { get; }
        public SavegameService Service { get; }
        public LocalInstance Instance { get; }

        public string SlotPath => Adapter.GetSlotPath(_slot);


        public void WriteManifest(int revision) => _manifestStore.Write(new SyncManifest
        {
            InstanceId = Instance.Id,
            RepoId = _repoId,
            ProfileId = _profileId,
            ProfileRevision = revision,
            SyncedAt = DateTimeOffset.UtcNow,
            ModFolder = _slots.Path,
            Entries = []
        });

        /// <summary>Records that this machine holds the savegame in the slot, at a known hash.</summary>
        public void Hold(string contentHash, int version = 1, int revision = 6)
            => _bindings.SetBinding(Instance.Id, new SavegameCheckoutBinding(
                _repoId,
                _savegameId,
                _slot.Value,
                version,
                contentHash,
                DateTime.UtcNow)
            {
                ProfileId = _profileId,
                ProfileRevision = revision
            });

        /// <summary>Writes the slot and returns what the packer says it hashes to.</summary>
        public async Task<string> WriteAndHashAsync(string content)
        {
            WriteSlotFile(content);

            return await new SavegamePacker().HashSlotAsync(Adapter, _slot, CancellationToken.None);
        }

        public void WriteSlotFile(string content)
        {
            var path = Path.Combine(SlotPath, "careerSavegame.xml");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            _slots.Dispose();
            _manifests.Dispose();
        }
    }
}
