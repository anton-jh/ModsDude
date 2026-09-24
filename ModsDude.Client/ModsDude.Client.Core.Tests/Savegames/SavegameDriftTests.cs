using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>
/// The drift states, both as the pure rule and as the check that runs on a real disk.
/// </summary>
public class SavegameDriftTests
{
    private static readonly SavegameSlotRef _slot = Keys.Slot("savegame1");

    private static readonly Guid _repoId = Guid.NewGuid();
    private static readonly Guid _savegameId = Guid.NewGuid();
    private static readonly Guid _profileId = Guid.NewGuid();


    [Fact]
    public void A_slot_that_still_holds_what_was_written_into_it_has_not_drifted()
    {
        var kinds = SavegameDriftRules.Classify(Binding(), "aaaa", headSnapshot: 4, _profileId, appliedRevision: 6);

        Assert.Empty(kinds);
    }

    /// <summary>
    /// Somebody's evening, and it exists nowhere else until it is checked in.
    /// </summary>
    [Fact]
    public void A_slot_whose_contents_have_moved_is_unchecked_in_play()
    {
        var kinds = SavegameDriftRules.Classify(Binding(), "bbbb", headSnapshot: 4, _profileId, appliedRevision: 6);

        Assert.Equal([SavegameDriftKind.UncheckedInPlay], kinds);
    }

    /// <summary>
    /// Every hash in the client is minted by <c>ModContentHasher</c> in one format, so two spellings
    /// are not one hash written twice - they are a bug in whichever part wrote the odd one. Absorbing
    /// the difference here would hide that, and would leave the next comparison free to lean on the
    /// same leniency.
    /// </summary>
    [Fact]
    public void A_hash_in_another_casing_is_another_hash()
    {
        Assert.Equal(
            [SavegameDriftKind.UncheckedInPlay],
            SavegameDriftRules.Classify(Binding(hash: "AAAA"), "aaaa", null, null, null));
    }

    /// <summary>
    /// The two hashes answer two questions, and this is the one that is asked here: does the slot
    /// still hold the snapshot the server has? An apply has just moved the observation boundary to the
    /// played bytes - so nothing further will be attributed - and the evening is still an evening
    /// that exists on this disk and nowhere else. One field could not say both.
    /// </summary>
    [Fact]
    public void An_observation_since_the_check_out_does_not_answer_this_question()
    {
        var binding = Binding(hash: "aaaa") with { LastObservedHash = "bbbb" };

        Assert.Equal(
            [SavegameDriftKind.UncheckedInPlay],
            SavegameDriftRules.Classify(binding, "bbbb", headSnapshot: 4, _profileId, appliedRevision: 6));
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
    public void A_head_past_the_snapshot_being_held_is_a_takeover()
    {
        var kinds = SavegameDriftRules.Classify(Binding(snapshot: 4), "aaaa", headSnapshot: 5, _profileId, appliedRevision: 6);

        Assert.Equal([SavegameDriftKind.TakenOverAndCheckedIn], kinds);
    }

    /// <summary>
    /// A client holding a head number older than the binding has not refreshed; inventing a takeover
    /// out of that would fire the notice on stale data rather than on anything that happened.
    /// </summary>
    [Fact]
    public void A_head_at_or_behind_the_held_snapshot_is_not_a_takeover()
    {
        Assert.Empty(SavegameDriftRules.Classify(Binding(snapshot: 4), "aaaa", headSnapshot: 4, _profileId, 6));
        Assert.Empty(SavegameDriftRules.Classify(Binding(snapshot: 4), "aaaa", headSnapshot: 3, _profileId, 6));
    }

    /// <summary>
    /// The moment somebody else takes the claim there are two copies of one save, whether or not
    /// either side has played - so it is reported before anybody checks in, not after.
    /// </summary>
    [Fact]
    public void A_claim_somebody_else_holds_is_a_takeover()
    {
        var kinds = SavegameDriftRules.Classify(Binding(), "aaaa", headSnapshot: 4, _profileId, 6, Claim("bob"));

        Assert.Equal([SavegameDriftKind.TakenOver], kinds);
    }

    /// <summary>
    /// The binding here is a claim this machine took, and the only way it ends while the binding stands
    /// is somebody taking it. Letting it go afterwards does not give it back.
    /// </summary>
    [Fact]
    public void A_claim_nobody_holds_is_a_takeover_that_was_let_go()
    {
        var kinds = SavegameDriftRules.Classify(Binding(), "aaaa", headSnapshot: 4, _profileId, 6, new SavegameClaimSighting(null, IsYours: false));

        Assert.Equal([SavegameDriftKind.TakenOver], kinds);
    }

    /// <summary>
    /// Yours on another machine is still yours - a claim is the person's, not the disk's.
    /// </summary>
    [Fact]
    public void A_claim_of_your_own_is_not_a_takeover()
    {
        Assert.Empty(SavegameDriftRules.Classify(Binding(), "aaaa", headSnapshot: 4, _profileId, 6, Claim("me", isYours: true)));
    }

    /// <summary>
    /// Checked in by the other side is the same event gone one step further, and saying both would be
    /// one takeover told twice.
    /// </summary>
    [Fact]
    public void A_takeover_that_has_since_been_checked_in_is_said_once()
    {
        var kinds = SavegameDriftRules.Classify(Binding(snapshot: 4), "aaaa", headSnapshot: 5, _profileId, 6, Claim("bob"));

        Assert.Equal([SavegameDriftKind.TakenOverAndCheckedIn], kinds);
    }

    /// <summary>
    /// A past savegame's revision does not move, and the apply table forbids moving it - so a folder
    /// that is off it is an interrupted sync or discarded local state rather than anybody's choice.
    /// The case that corrupts saves, and it costs no I/O whatsoever: both numbers are already in local
    /// state.
    /// </summary>
    [Fact]
    public void A_past_savegame_whose_folder_left_its_revision_is_drift()
    {
        var kinds = SavegameDriftRules.Classify(Binding(target: 6), "aaaa", headSnapshot: 4, _profileId, appliedRevision: 8);

        Assert.Equal([SavegameDriftKind.PlayedOnAnotherModList], kinds);
    }

    /// <summary>
    /// <b>The false alarm this rule used to fire.</b> A savegame checked out at revision 6 whose profile is
    /// then applied at revision 8 is a savegame following its profile exactly as intended - that is what
    /// current means - and reporting it here spends the app's loudest warning on the ordinary flow.
    /// Being behind head is the game's business, and <c>profileHasMoved</c> already says it there.
    /// </summary>
    [Fact]
    public void A_current_savegame_whose_profile_moved_underneath_it_is_not_this_drift()
    {
        Assert.Empty(SavegameDriftRules.Classify(Binding(revision: 6), "aaaa", headSnapshot: 4, _profileId, appliedRevision: 8));
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
        var kinds = SavegameDriftRules.Classify(Binding(snapshot: 4), "bbbb", headSnapshot: 5, _profileId, appliedRevision: 6);

        Assert.Equal(
            [SavegameDriftKind.UncheckedInPlay, SavegameDriftKind.TakenOverAndCheckedIn],
            kinds);
    }


    [Fact]
    public async Task A_game_holding_nothing_reports_nothing()
    {
        using var harness = new DriftHarness();

        Assert.Empty(await harness.Service.CheckDriftAsync(harness.Game.Identity, CancellationToken.None));
    }

    [Fact]
    public async Task Play_in_a_held_slot_is_found_by_hashing_it()
    {
        using var harness = new DriftHarness();

        harness.Hold(await harness.WriteAndHashAsync("a savegame"));
        harness.WriteSlotFile("a savegame, played once");

        var drift = Assert.Single(await harness.Service.CheckDriftAsync(harness.Game.Identity, CancellationToken.None));

        Assert.Equal(SavegameDriftKind.UncheckedInPlay, drift.Kind);
        Assert.Equal(_slot, drift.Slot);
        Assert.Equal(_savegameId, drift.SavegameId);
    }

    [Fact]
    public async Task An_untouched_held_slot_reports_nothing()
    {
        using var harness = new DriftHarness();

        harness.Hold(await harness.WriteAndHashAsync("a savegame"));

        Assert.Empty(await harness.Service.CheckDriftAsync(harness.Game.Identity, CancellationToken.None));
    }

    [Fact]
    public async Task A_head_the_client_knows_has_moved_past_the_binding_is_reported()
    {
        using var harness = new DriftHarness();

        harness.Hold(await harness.WriteAndHashAsync("a savegame"), snapshot: 3);
        harness.Sightings.Set(_savegameId, 4);

        var drift = Assert.Single(await harness.Service.CheckDriftAsync(harness.Game.Identity, CancellationToken.None));

        Assert.Equal(SavegameDriftKind.TakenOverAndCheckedIn, drift.Kind);
        Assert.Equal(3, drift.HeldSnapshot);
        Assert.Equal(4, drift.HeadSnapshot);
    }

    [Fact]
    public async Task A_claim_the_client_saw_somebody_else_holding_is_reported_naming_them()
    {
        using var harness = new DriftHarness();

        harness.Hold(await harness.WriteAndHashAsync("a savegame"), snapshot: 3);
        harness.Sightings.Set(_savegameId, 3);
        harness.Sightings.SetClaim(_savegameId, Claim("bob"));

        var drift = Assert.Single(await harness.Service.CheckDriftAsync(harness.Game.Identity, CancellationToken.None));

        Assert.Equal(SavegameDriftKind.TakenOver, drift.Kind);
        Assert.Equal("Bob", drift.TakenBy?.DisplayName);
    }

    /// <summary>
    /// No hashing, no network, no directory listing beyond the slot list: two integers already in
    /// local state. It is the reason this state is worth having at all.
    /// </summary>
    [Fact]
    public async Task A_past_savegame_whose_folder_was_re_synced_off_its_revision_is_reported()
    {
        using var harness = new DriftHarness();

        harness.Hold(await harness.WriteAndHashAsync("a savegame"), revision: 6, target: 6);
        harness.WriteManifest(revision: 8);

        var drift = Assert.Single(await harness.Service.CheckDriftAsync(harness.Game.Identity, CancellationToken.None));

        Assert.Equal(SavegameDriftKind.PlayedOnAnotherModList, drift.Kind);
        Assert.Equal(6, drift.TargetRevision);
        Assert.Equal(8, drift.AppliedRevision);
    }

    /// <summary>
    /// The same two integers for a savegame that is its profile's current savegame say nothing at all: it
    /// follows the profile, so the folder moving to a newer revision of it is the intended flow.
    /// </summary>
    [Fact]
    public async Task A_current_savegame_whose_folder_moved_to_a_newer_revision_reports_nothing()
    {
        using var harness = new DriftHarness();

        harness.Hold(await harness.WriteAndHashAsync("a savegame"), revision: 6);
        harness.WriteManifest(revision: 8);

        Assert.Empty(await harness.Service.CheckDriftAsync(harness.Game.Identity, CancellationToken.None));
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

        harness.Hold(await harness.WriteAndHashAsync("a savegame"));

        Directory.Delete(harness.SlotPath, recursive: true);

        Assert.Empty(await harness.Service.CheckDriftAsync(harness.Game.Identity, CancellationToken.None));
    }

    /// <summary>
    /// Savegame drift reaches the notice on the same report as mod drift, so that one sentence can
    /// carry both halves of "this folder is not what you think it is".
    /// </summary>
    [Fact]
    public void Savegame_drift_rides_on_the_game_drift_report()
    {
        using var manifests = new TempDirectory("savegame-drift-report");
        using var modFolder = new TempDirectory("savegame-drift-mods");

        var service = new DriftService(new SyncManifestStore(manifests.Path), NullLogger<DriftService>.Instance);
        var drift = new[] { new SavegameDrift(_repoId, _savegameId, _slot, SavegameDriftKind.UncheckedInPlay) };

        var report = service.Check(
            Keys.Target(),
            new ActiveProfile(_repoId, _profileId),
            modFolder.Path,
            savegameDrift: drift);

        // Never synced, so the mod half has nothing to say - and the savegame half is carried anyway.
        Assert.Equal(DriftStatus.NeverSynced, report.Status);
        Assert.True(report.HasSavegameDrift);
        Assert.Equal(SavegameDriftKind.UncheckedInPlay, Assert.Single(report.SavegameDrift).Kind);

        // And it is enough on its own to make the notice fire, without pretending the mod folder
        // drifted.
        var target = new GameModFolder(Keys.Target(), modFolder.Path);

        Assert.True(new TargetDrift(
            new DriftCandidate(Keys.Game(), "FS25", [target], null),
            target,
            report,
            null).IsDrifted);
    }


    /// <param name="target">
    /// What the savegame runs on. A number is a past savegame, pinned; null is a current one, which
    /// follows its profile and pins nothing.
    /// </param>
    private static SavegameCheckoutBinding Binding(
        int snapshot = 4, string hash = "aaaa", int? revision = 6, int? target = null) => new(
        _repoId,
        _savegameId,
        _slot,
        snapshot,
        hash,
        DateTime.UtcNow)
    {
        ProfileId = _profileId,
        ProfileRevision = revision,
        TargetRevision = target
    };


    private static SavegameClaimSighting Claim(string userId, bool isYours = false) => new(
        new SavegameClaimHolder(userId, isYours ? "Me" : "Bob", DateTime.UtcNow.AddHours(-2)),
        isYours);


    /// <summary>The drift check over a real slot folder, a real packer and real local state.</summary>
    private sealed class DriftHarness : IDisposable
    {
        private readonly TempDirectory _slots = new("savegame-drift-slots");
        private readonly TempDirectory _manifests = new("savegame-drift-manifests");

        private readonly SavegameBindingStore _bindings;
        private readonly SyncManifestStore _manifestStore;


        public DriftHarness()
        {
            var persisted = new PersistedGame
            {
                GameAdapterId = new GameAdapterId("farmingSimulator", 1),
                Name = "Farming Simulator 25",
                AdapterLocalSettings = "{}",
                Targets = [new PersistedModTarget(Keys.Target().Key, _slots.Path)],
                ActiveProfile = new ActiveProfile(_repoId, _profileId)
            };

            State.Add(Keys.Game(), persisted);
            Game = new Game(Keys.Game(), persisted);

            Adapter = new FakeSavegameAdapter(_slots.Path, _slot.Slot.Value);
            _bindings = new SavegameBindingStore(State);
            _manifestStore = new SyncManifestStore(_manifests.Path);

            WriteManifest(revision: 6);

            Service = new SavegameService(
                Server,
                Server,
                new SavegamePacker(),
                _bindings,
                new FakeSavegameAdapters(Adapter),
                new FakeSavegameDownloader(Server),
                new FakeSavegameUploader(Server),
                _manifestStore,
                new FakeSlotRecycleBin(),
                NullLogger<SavegameService>.Instance,
                Sightings);
        }


        public FakeSavegameServer Server { get; } = new();
        public FakeSavegameSightings Sightings { get; } = new();
        public FakeGameState State { get; } = new();
        public FakeSavegameAdapter Adapter { get; }
        public SavegameService Service { get; }
        public Game Game { get; }

        public SavegameTarget Target => Adapter.SavegameTargets[_slot.Target]!;

        public string SlotPath => Adapter.GetSlotPath(Target, _slot.Slot);


        public void WriteManifest(int revision) => _manifestStore.Write(new SyncManifest
        {
            Target = Keys.Target(),
            RepoId = _repoId,
            ProfileId = _profileId,
            ProfileRevision = revision,
            SyncedAt = DateTimeOffset.UtcNow,
            ModFolder = _slots.Path,
            Entries = []
        });

        /// <summary>Records that this machine holds the savegame in the slot, at a known hash.</summary>
        /// <param name="target">A number makes it a past savegame, pinned to that revision.</param>
        public void Hold(string contentHash, int snapshot = 1, int revision = 6, int? target = null)
            => _bindings.SetBinding(Game.Identity, new SavegameCheckoutBinding(
                _repoId,
                _savegameId,
                _slot,
                snapshot,
                contentHash,
                DateTime.UtcNow)
            {
                ProfileId = _profileId,
                ProfileRevision = revision,
                TargetRevision = target
            });

        /// <summary>Writes the slot and returns what the packer says it hashes to.</summary>
        public async Task<string> WriteAndHashAsync(string content)
        {
            WriteSlotFile(content);

            return await new SavegamePacker().HashSlotAsync(Adapter, Target, _slot.Slot, CancellationToken.None);
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
