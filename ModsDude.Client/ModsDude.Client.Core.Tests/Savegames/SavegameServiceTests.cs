using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>
/// The four verbs, end to end against a real packer on a real disk and a fake server that reproduces
/// the server's actual rules.
/// </summary>
/// <remarks>
/// The packer is real rather than mocked because most of what is being asserted here is only true if
/// packing round-trips: a check-in that skips the upload does so because the bytes it packed hash to
/// what is already stored, and a slot that reads as clean after a check-out does so because unpacking
/// and repacking produce the same archive. A mocked packer would agree with whatever the test
/// assumed, which is precisely the assumption worth checking.
/// </remarks>
public class SavegameServiceTests
{
    private static readonly SavegameSlotId _slot1 = new("savegame1");
    private static readonly SavegameSlotId _slot2 = new("savegame2");


    [Fact]
    public async Task Checking_out_takes_the_claim_writes_the_slot_and_records_what_it_wrote()
    {
        using var harness = new Harness();
        var head = await harness.SeedHeadAsync("a farm");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        Assert.Equal(1, harness.Server.CheckoutsTaken);
        Assert.Equal("a farm", harness.ReadSlotFile(_slot1));

        var binding = harness.Service.GetBinding(harness.Instance, harness.Server.SavegameId);

        Assert.NotNull(binding);
        Assert.Equal(_slot1.Value, binding.Value.SlotId);
        Assert.Equal(head.Number, binding.Value.Version);
        Assert.Equal(head.ContentHash, binding.Value.ContentHash);

        // The two facts the third drift state needs, and the only place they can be recorded: asking
        // the server which revision a held version was played on is a network call in a check that
        // has to work offline.
        Assert.Equal(head.ProfileId, binding.Value.ProfileId);
        Assert.Equal(head.ProfileRevision, binding.Value.ProfileRevision);
    }

    /// <summary>
    /// The refusal that is the point of the whole safety check. The slot holds an evening that exists
    /// nowhere else, and the remedy is to check that savegame in - which is an action, not a warning.
    /// </summary>
    [Fact]
    public async Task Checking_out_over_unpublished_play_is_refused_before_the_claim_is_taken()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a farm");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        // An evening in the slot: the contents no longer hash to what was written there.
        harness.WriteSlotFile(_slot1, "a farm, and a barn");

        var other = harness.Server.Savegame with { Id = Guid.NewGuid(), Name = "Season 5" };

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.Service.CheckOutAsync(harness.Instance, other, _slot1, CancellationToken.None));

        Assert.Contains("nobody has checked in", exception.UserMessage);

        // The destructive step is local and comes first, so nothing was claimed on anybody's behalf
        // and the slot still holds the evening.
        Assert.Equal(1, harness.Server.CheckoutsTaken);
        Assert.Equal("a farm, and a barn", harness.ReadSlotFile(_slot1));
    }

    [Fact]
    public async Task A_free_slot_and_an_unrecognised_one_are_told_apart()
    {
        using var harness = new Harness();

        Assert.Equal(SavegameSlotAvailability.Free, await harness.Service.ClassifySlotAsync(harness.Instance, _slot1, CancellationToken.None));

        harness.WriteSlotFile(_slot1, "somebody's own farm");

        Assert.Equal(SavegameSlotAvailability.Unrecognised, await harness.Service.ClassifySlotAsync(harness.Instance, _slot1, CancellationToken.None));
    }

    /// <summary>
    /// A check-out followed by nothing leaves the slot holding exactly what was written, which has to
    /// read as clean - if it did not, every check-out would immediately report unpublished play and
    /// the notice would be worthless.
    /// </summary>
    [Fact]
    public async Task A_slot_just_checked_out_into_reads_as_held_and_clean()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a farm");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        Assert.Equal(SavegameSlotAvailability.HeldClean, await harness.Service.ClassifySlotAsync(harness.Instance, _slot1, CancellationToken.None));
    }

    [Fact]
    public async Task The_picker_pre_selects_the_remembered_slot_and_falls_back_to_the_first_free_one()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a farm");

        // Nothing remembered yet: the first free slot.
        Assert.Equal(_slot1, await harness.Service.SuggestSlotAsync(harness.Instance, harness.Server.SavegameId, CancellationToken.None));

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot2, CancellationToken.None);
        await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None);

        // The hint survives the check-in that destroyed the binding - that asymmetry is its whole job.
        Assert.Equal(_slot2, await harness.Service.SuggestSlotAsync(harness.Instance, harness.Server.SavegameId, CancellationToken.None));

        // And when the remembered slot is taken by something else, the first free one instead. The
        // hint is left exactly as it was; nothing here repairs it.
        harness.WriteSlotFile(_slot2, "somebody's own farm");

        Assert.Equal(_slot1, await harness.Service.SuggestSlotAsync(harness.Instance, harness.Server.SavegameId, CancellationToken.None));
    }

    [Fact]
    public async Task No_free_slot_pre_selects_nothing()
    {
        using var harness = new Harness();

        harness.WriteSlotFile(_slot1, "one farm");
        harness.WriteSlotFile(_slot2, "another farm");

        Assert.Null(await harness.Service.SuggestSlotAsync(harness.Instance, harness.Server.SavegameId, CancellationToken.None));
    }

    /// <summary>
    /// <b>The whole point of addressing the blob by its content.</b> A night that changed nothing must
    /// not cost a 400 MB upload, and the server says so by answering the upload link request with
    /// <c>alreadyStored</c>.
    /// </summary>
    [Fact]
    public async Task Checking_in_unchanged_bytes_skips_the_upload_entirely()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a farm");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        var versionsBefore = harness.Server.Versions.Count;

        await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None);

        Assert.Equal(1, harness.Server.UploadLinksMinted);
        Assert.Equal(0, harness.Uploader.Uploads);

        // And the server minted nothing either: a save that changes nothing costs no line of history.
        Assert.Equal(versionsBefore, harness.Server.Versions.Count);
    }

    [Fact]
    public async Task Checking_in_played_bytes_uploads_them_and_mints_a_version_based_on_what_was_held()
    {
        using var harness = new Harness();
        var head = await harness.SeedHeadAsync("a farm");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a farm, and a barn");

        var version = await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, "after the barn", keepPlaying: false, force: false, CancellationToken.None);

        Assert.Equal(1, harness.Uploader.Uploads);
        Assert.Equal(head.Number + 1, version.Number);
        Assert.Equal("after the barn", version.Label);

        // Based on the version that was actually in the slot, which is the mechanical half of the
        // one-holder-at-a-time guarantee - the checkout is only the social half.
        Assert.Equal(head.Number, Assert.Single(harness.Server.CheckIns).BasedOn);

        // And the revision the folder is actually on, from the manifest.
        Assert.Equal(harness.AppliedRevision, harness.Server.CheckIns[0].ProfileRevision);
    }

    /// <summary>
    /// <b>Only after the upload is verified</b>, which here means after the commit: a blob no version
    /// names is unreachable, so the upload alone is not the moment.
    /// </summary>
    [Fact]
    public async Task Checking_in_recycles_the_local_copy_only_after_the_commit()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a farm");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a farm, and a barn");

        await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None);

        Assert.Equal(harness.SlotPath(_slot1), Assert.Single(harness.RecycleBin.Recycled));
        Assert.False(Directory.Exists(harness.SlotPath(_slot1)));

        // The slot is free again, which is what removes any need for eviction machinery.
        Assert.Null(harness.Service.GetBinding(harness.Instance, harness.Server.SavegameId));
        Assert.Equal(SavegameSlotAvailability.Free, await harness.Service.ClassifySlotAsync(harness.Instance, _slot1, CancellationToken.None));
    }

    /// <summary>
    /// The other half of the same rule. A refused check-in must leave the evening exactly where it
    /// was: recycling on the way out would destroy the only copy of play the server just refused.
    /// </summary>
    [Fact]
    public async Task A_refused_check_in_recycles_nothing_and_keeps_the_binding()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a farm");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a farm, and a barn");

        // Somebody took the save over and checked in while this machine was playing.
        harness.Server.CheckInFromAnotherMachine(await harness.PackedBytesAsync("somebody else's evening"));

        var exception = await Assert.ThrowsAsync<ApiException<CustomProblemDetails>>(
            () => harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: false, force: false, CancellationToken.None));

        // Surfaced, never swallowed: forcing past a moved head is a decision only the person holding
        // the save can make, and the caller can only offer it if it can tell this failure apart.
        Assert.True(SavegameService.IsVersionStale(exception));

        Assert.Empty(harness.RecycleBin.Recycled);
        Assert.Equal("a farm, and a barn", harness.ReadSlotFile(_slot1));
        Assert.NotNull(harness.Service.GetBinding(harness.Instance, harness.Server.SavegameId));
    }

    [Fact]
    public async Task Forcing_past_a_moved_head_checks_in_and_records_the_fork()
    {
        using var harness = new Harness();
        var head = await harness.SeedHeadAsync("a farm");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a farm, and a barn");
        harness.Server.CheckInFromAnotherMachine(await harness.PackedBytesAsync("somebody else's evening"));

        var version = await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: false, force: true, CancellationToken.None);

        Assert.Equal(SavegameVersionOrigin.Forced, version.Origin);
        Assert.Equal(head.Number, version.BaseVersion);
    }

    /// <summary>
    /// For somebody who wants tonight's progress on the server and intends to carry on. The binding
    /// has to be rebased, or the next check-in is based on a version that is no longer the head and is
    /// refused for a takeover that never happened.
    /// </summary>
    [Fact]
    public async Task Checking_in_and_carrying_on_keeps_the_binding_and_rebases_it()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a farm");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        harness.WriteSlotFile(_slot1, "a farm, and a barn");

        var version = await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: true, force: false, CancellationToken.None);

        var binding = harness.Service.GetBinding(harness.Instance, harness.Server.SavegameId);

        Assert.NotNull(binding);
        Assert.Equal(version.Number, binding.Value.Version);
        Assert.Equal(version.ContentHash, binding.Value.ContentHash);

        // Nothing was recycled, and the slot still reads as held and clean - which is exactly what
        // "carry on playing" has to mean.
        Assert.Empty(harness.RecycleBin.Recycled);
        Assert.Equal(SavegameSlotAvailability.HeldClean, await harness.Service.ClassifySlotAsync(harness.Instance, _slot1, CancellationToken.None));

        // And a second check-in is based on the first, not on the version that was checked out.
        harness.WriteSlotFile(_slot1, "a farm, a barn, and a field");

        await harness.Service.CheckInAsync(harness.Instance, harness.Server.SavegameId, null, keepPlaying: true, force: false, CancellationToken.None);

        Assert.Equal(version.Number, harness.Server.CheckIns[^1].BasedOn);
    }

    [Fact]
    public async Task Checking_in_a_savegame_this_machine_does_not_hold_is_refused_with_a_sentence()
    {
        using var harness = new Harness();

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.Service.CheckInAsync(harness.Instance, Guid.NewGuid(), null, keepPlaying: false, force: false, CancellationToken.None));

        Assert.Contains("not holding", exception.UserMessage);
    }

    /// <summary>
    /// Taken by mistake, never played. Without this the only ways out are a junk version and waiting
    /// to be taken over.
    /// </summary>
    [Fact]
    public async Task Discarding_ends_the_checkout_and_mints_no_version()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a farm");

        await harness.Service.CheckOutAsync(harness.Instance, harness.Server.Savegame, _slot1, CancellationToken.None);

        var versionsBefore = harness.Server.Versions.Count;

        await harness.Service.DiscardAsync(harness.Instance, harness.Server.SavegameId, CancellationToken.None);

        Assert.Equal(1, harness.Server.CheckoutsDiscarded);
        Assert.Equal(versionsBefore, harness.Server.Versions.Count);
        Assert.Empty(harness.Server.CheckIns);

        // Nothing was uploaded either - a discard is not a check-in with a shrug.
        Assert.Equal(0, harness.Uploader.Uploads);

        Assert.Null(harness.Service.GetBinding(harness.Instance, harness.Server.SavegameId));
        Assert.Equal(harness.SlotPath(_slot1), Assert.Single(harness.RecycleBin.Recycled));
    }

    [Fact]
    public async Task Publishing_uploads_the_slot_mints_the_savegame_and_leaves_this_machine_holding_it()
    {
        using var harness = new Harness();

        harness.WriteSlotFile(_slot1, "a brand new farm");

        var savegame = await harness.Service.PublishAsync(harness.Instance, _slot1, "Season 5", "the beginning", CancellationToken.None);

        Assert.Equal(1, harness.Uploader.Uploads);
        Assert.Equal("Season 5", savegame.Name);

        var request = Assert.Single(harness.Server.Publishes);

        // The id is minted client-side, because the blob is addressed by it and has to be uploadable
        // before the savegame exists.
        Assert.NotEqual(Guid.Empty, request.SavegameId);
        Assert.Equal(savegame.Id, request.SavegameId);

        // Nothing was asked about the profile: the instance has an active one and a manifest saying
        // which revision of it this folder is on.
        Assert.Equal(harness.ProfileId, request.ProfileId);
        Assert.Equal(harness.AppliedRevision, request.ProfileRevision);

        // Publishing leaves you holding it - the server opens a claim, and this is its local half.
        var binding = harness.Service.GetBinding(harness.Instance, savegame.Id);

        Assert.NotNull(binding);
        Assert.Equal(_slot1.Value, binding.Value.SlotId);
        Assert.Equal(SavegameSlotAvailability.HeldClean, await harness.Service.ClassifySlotAsync(harness.Instance, _slot1, CancellationToken.None));
    }

    [Fact]
    public async Task Publishing_from_an_instance_that_has_never_been_synced_is_refused()
    {
        using var harness = new Harness(writeManifest: false);

        harness.WriteSlotFile(_slot1, "a brand new farm");

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.Service.PublishAsync(harness.Instance, _slot1, "Season 5", null, CancellationToken.None));

        Assert.Contains("not been synced", exception.UserMessage);
        Assert.Empty(harness.Server.Publishes);
    }

    /// <summary>
    /// What a Guest gets, and what looking at an old version without disturbing anybody looks like:
    /// bytes in a slot, no claim, no binding, and therefore nothing that can be checked in from it.
    /// </summary>
    [Fact]
    public async Task Taking_a_copy_claims_nothing_and_binds_nothing()
    {
        using var harness = new Harness();
        var first = await harness.SeedHeadAsync("a farm");

        harness.Server.CheckInFromAnotherMachine(await harness.PackedBytesAsync("a farm, and a barn"));

        await harness.Service.TakeCopyAsync(harness.Instance, harness.Server.Savegame, first.Number, _slot1, CancellationToken.None);

        Assert.Equal("a farm", harness.ReadSlotFile(_slot1));
        Assert.Equal(0, harness.Server.CheckoutsTaken);
        Assert.Empty(harness.Service.GetBindings(harness.Instance));

        // An ordinary unrecognised slot afterwards, which is the honest description: ModsDude has no
        // claim on what is in it and no way to hand it back.
        Assert.Equal(SavegameSlotAvailability.Unrecognised, await harness.Service.ClassifySlotAsync(harness.Instance, _slot1, CancellationToken.None));
    }

    [Fact]
    public async Task Taking_a_copy_of_a_pruned_version_says_so()
    {
        using var harness = new Harness();
        await harness.SeedHeadAsync("a farm");

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(
            () => harness.Service.TakeCopyAsync(harness.Instance, harness.Server.Savegame, 99, _slot1, CancellationToken.None));

        Assert.Contains("not there any more", exception.UserMessage);
    }


    /// <summary>
    /// A real disk, a real packer and a fake server, wired the way the app wires them.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        private readonly TempDirectory _slots = new("savegame-service-slots");
        private readonly TempDirectory _manifests = new("savegame-service-manifests");


        public Harness(bool writeManifest = true)
        {
            var persisted = new PersistedLocalInstance
            {
                Id = Guid.NewGuid(),
                Scope = new InstanceScope("farmingSimulator", "fs25"),
                GameAdapterId = new GameAdapterId("farmingSimulator", 1),
                Name = "Farming Simulator 25",
                AdapterInstanceSettings = "{}",
                ModFolder = _slots.Path,
                ActiveProfile = new ActiveProfile(Server.RepoId, Server.ProfileId)
            };

            State.Add(persisted);
            Instance = new LocalInstance(persisted);

            Uploader = new FakeSavegameUploader(Server);
            Adapter = new FakeSavegameAdapter(_slots.Path, _slot1.Value, _slot2.Value);
            Bindings = new SavegameBindingStore(State);
            ManifestStore = new SyncManifestStore(_manifests.Path);

            if (writeManifest)
            {
                ManifestStore.Write(new SyncManifest
                {
                    InstanceId = Instance.Id,
                    RepoId = Server.RepoId,
                    ProfileId = Server.ProfileId,
                    ProfileRevision = AppliedRevision,
                    SyncedAt = DateTimeOffset.UtcNow,
                    ModFolder = _slots.Path,
                    Entries = []
                });
            }

            Service = new SavegameService(
                Server,
                Server,
                new SavegamePacker(),
                Bindings,
                new FakeInstanceSavegameAdapters(Adapter),
                new FakeSavegameDownloader(Server),
                Uploader,
                ManifestStore,
                RecycleBin,
                NullLogger<SavegameService>.Instance,
                Heads);
        }


        /// <summary>Which revision of the profile the mod folder is on, per the manifest.</summary>
        public int AppliedRevision => 1;

        public FakeSavegameServer Server { get; } = new();
        public FakeSavegameUploader Uploader { get; }
        public FakeSlotRecycleBin RecycleBin { get; } = new();
        public FakeSavegameHeadVersions Heads { get; } = new();
        public FakeInstanceState State { get; } = new();
        public FakeSavegameAdapter Adapter { get; }
        public SavegameBindingStore Bindings { get; }
        public SyncManifestStore ManifestStore { get; }
        public SavegameService Service { get; }
        public LocalInstance Instance { get; }

        public Guid ProfileId => Server.ProfileId;


        /// <summary>Puts a savegame on the server whose bytes are a real packed slot.</summary>
        public async Task<SavegameVersionDto> SeedHeadAsync(string content, int profileRevision = 1)
            => Server.Seed(await PackedBytesAsync(content), profileRevision);

        /// <summary>
        /// What a slot holding <paramref name="content"/> packs to. Built through the real packer in a
        /// staging slot, because the archive the server serves has to be one the packer would produce
        /// - otherwise a check-out followed by a check-in would look like play.
        /// </summary>
        public async Task<byte[]> PackedBytesAsync(string content)
        {
            var staging = new SavegameSlotId($"staging-{Guid.NewGuid():N}");

            WriteSlotFile(staging, content);

            var packed = await new SavegamePacker().PackAsync(Adapter, staging, CancellationToken.None);

            try
            {
                return await File.ReadAllBytesAsync(packed.FilePath);
            }
            finally
            {
                File.Delete(packed.FilePath);
                Directory.Delete(SlotPath(staging), recursive: true);
            }
        }

        public string SlotPath(SavegameSlotId slot) => Adapter.GetSlotPath(slot);

        public void WriteSlotFile(SavegameSlotId slot, string content)
        {
            var path = Path.Combine(SlotPath(slot), "careerSavegame.xml");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public string ReadSlotFile(SavegameSlotId slot)
            => File.ReadAllText(Path.Combine(SlotPath(slot), "careerSavegame.xml"));

        public void Dispose()
        {
            _slots.Dispose();
            _manifests.Dispose();
        }
    }
}
