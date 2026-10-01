using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameFiles;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Tests.GameProcesses;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>
/// The savegame services wired together as the app wires them, against a real packer on a real disk and
/// a fake server that reproduces the server's actual rules.
/// </summary>
/// <remarks>
/// The packer is real because most of what the tests assert is only true if packing round-trips: a
/// check-in skips the upload because the bytes it packed hash to what is already stored, and a slot
/// reads as clean after a check-out because unpacking and repacking produce the same archive.
/// </remarks>
internal sealed class SavegameHarness : IDisposable
{
    public static readonly SavegameSlotRef Slot1 = Keys.Slot("savegame1");
    public static readonly SavegameSlotRef Slot2 = Keys.Slot("savegame2");

    /// <summary>The same slot number in the game's other folder, which is what two targets look like.</summary>
    public static readonly SavegameSlotRef Client = Keys.Slot("savegame1", "client");

    private readonly TempDirectory _slots = new("savegame-service-slots");
    private readonly TempDirectory _clientSlots = new("savegame-service-client-slots");
    private readonly TempDirectory _manifests = new("savegame-service-manifests");
    private readonly TempDirectory _store = new("savegame-service-store");


    public SavegameHarness(bool writeManifest = true, int appliedRevision = 1)
    {
        AppliedRevision = appliedRevision;

        var persisted = new PersistedGame
        {
            GameAdapterId = new GameAdapterId("farmingSimulator", 1),
            Name = "Farming Simulator 25",
            AdapterLocalSettings = "{}",
            Targets = [new PersistedModTarget(Keys.Target().Key, _slots.Path)],
            ActiveProfile = new ActiveProfile(Server.RepoId, Server.ProfileId)
        };

        State.Add(Keys.Game(), persisted);
        Game = new Game(Keys.Game(), persisted);

        Uploader = new FakeSavegameUploader(Server);
        Adapter = new FakeSavegameAdapter(_slots.Path, Slot1.Slot.Value, Slot2.Slot.Value);
        Bindings = new SavegameBindingStore(State);
        ManifestStore = new SyncManifestStore(_manifests.Path);
        Store = new ContentStore("C:\\", _store.Path, long.MaxValue);

        if (writeManifest)
        {
            WriteManifest(appliedRevision);
        }

        var time = TimeProvider.System;
        var packer = new SavegamePacker();
        var adapters = new FakeSavegameAdapters(Adapter);
        Reader = new HeldSlotReader(adapters, Bindings, packer, NullLogger<HeldSlotReader>.Instance);
        var recycler = new SavegameRecycler(RecycleBin, new FakeStoreProvider(Store), packer, time, NullLogger<SavegameRecycler>.Instance)
        {
            RetryDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]
        };
        var transfer = new SavegameTransfer(Server, new FakeSavegameDownloader(Server), Uploader, packer, recycler, NullLogger<SavegameTransfer>.Instance);
        var renamer = new SavegameRenamer(new GameFileEditor(RecycleBin, NullLogger<GameFileEditor>.Instance), NullLogger<SavegameRenamer>.Instance);

        Slots = new SavegameSlots(adapters, Bindings, packer, NullLogger<SavegameSlots>.Instance);
        HeldSavegames = new HeldSavegames(Bindings);
        PlayAttribution = new SavegamePlayAttribution(Bindings, Reader, ManifestStore, NullLogger<SavegamePlayAttribution>.Instance);
        DriftCheck = new SavegameDriftCheck(Reader, ManifestStore, Sightings);
        Holds = new SavegameHolds(Server, Bindings, adapters, NullLogger<SavegameHolds>.Instance);
        CheckOut = new SavegameCheckOut(Server, Bindings, adapters, Slots, transfer, Sightings, Guard, time);
        CheckIn = new SavegameCheckIn(
            Server, packer, Bindings, adapters, Slots, transfer, recycler, renamer, Holds, PlayAttribution, Guard, time,
            NullLogger<SavegameCheckIn>.Instance);
        Publisher = new SavegamePublisher(
            Server, packer, Bindings, adapters, Slots, transfer, recycler, renamer, Holds, Guard, time,
            NullLogger<SavegamePublisher>.Instance);
    }


    /// <summary>Which revision of the profile the mod folder is on, per the manifest.</summary>
    public int AppliedRevision { get; private set; }

    public FakeSavegameServer Server { get; } = new();
    public FakeSavegameUploader Uploader { get; }
    public FakeSlotRecycleBin RecycleBin { get; } = new();
    public FakeSavegameSightings Sightings { get; } = new();
    public FakeGameRunningGuard Guard { get; } = new();
    public FakeGameState State { get; } = new();
    public FakeSavegameAdapter Adapter { get; }
    public SavegameBindingStore Bindings { get; }
    public SyncManifestStore ManifestStore { get; }
    public ContentStore Store { get; }
    public Game Game { get; }

    public SavegameSlots Slots { get; }
    public HeldSlotReader Reader { get; }
    public HeldSavegames HeldSavegames { get; }
    public SavegamePlayAttribution PlayAttribution { get; }
    public SavegameDriftCheck DriftCheck { get; }
    public SavegameHolds Holds { get; }
    public SavegameCheckOut CheckOut { get; }
    public SavegameCheckIn CheckIn { get; }
    public SavegamePublisher Publisher { get; }

    public Guid ProfileId => Server.ProfileId;


    /// <summary>
    /// What the publish dialog settles: which profile the new savegame follows, and the revision its
    /// first snapshot declares. Through the shared rule, so the tests exercise what the dialog shows.
    /// </summary>
    public SavegamePublishTarget Target(int headRevision = 1)
    {
        var manifest = ManifestStore.TryRead(Keys.Target());

        return new SavegamePublishTarget(
            ProfileId,
            SavegameRevisionRules.DeclaredRevisionFor(ProfileId, headRevision, manifest?.ProfileId, manifest?.ProfileRevision));
    }

    /// <summary>A second folder pair under this game, numbered exactly like the first.</summary>
    public void AddSecondTarget() => Adapter.AddTarget(Client.Target, _clientSlots.Path);

    /// <summary>The settings edit that takes it away again, with whatever was checked out into it still on disk.</summary>
    public void RemoveSecondTarget() => Adapter.RemoveTarget(Client.Target);

    /// <summary>Puts a savegame on the server whose bytes are a real packed slot.</summary>
    public async Task<SavegameSnapshotDto> SeedHeadAsync(string content, int? profileRevision = 1)
        => Server.Seed(await PackedBytesAsync(content), profileRevision);

    /// <summary>
    /// What applying a profile does to this game, in the sync engine's order: attribute what was played
    /// on the outgoing revision, then say the folder is on the incoming one.
    /// </summary>
    public async Task ApplyAsync(int revision, string target = "mods")
    {
        await PlayAttribution.ObserveAsync(Keys.Target(target), CancellationToken.None);

        WriteManifest(Server.ProfileId, revision, target);
    }

    public void WriteManifest(int revision) => WriteManifest(Server.ProfileId, revision);

    /// <summary>The game re-pointed at a different profile and synced to it.</summary>
    public void PointTheFolderAtAnotherProfile(int revision) => WriteManifest(Guid.NewGuid(), revision);

    /// <param name="target">Which of the game's folders was applied to.</param>
    public void WriteManifest(Guid profileId, int revision, string target = "mods")
    {
        AppliedRevision = revision;

        ManifestStore.Write(new SyncManifest
        {
            Target = Keys.Target(target),
            RepoId = Server.RepoId,
            ProfileId = profileId,
            ProfileRevision = revision,
            SyncedAt = DateTimeOffset.UtcNow,
            ModFolder = _slots.Path,
            Entries = []
        });
    }

    /// <summary>What this machine records about a savegame it is holding.</summary>
    public SavegameCheckoutBinding Binding(Guid savegameId)
        => Bindings.GetBinding(Game.Identity, savegameId) ?? throw new InvalidOperationException("Nothing is held.");

    /// <summary>
    /// What a slot holding <paramref name="content"/> packs to, built through the real packer so a
    /// check-out followed by a check-in does not look like play.
    /// </summary>
    public async Task<byte[]> PackedBytesAsync(string content)
    {
        var staging = Keys.Slot($"staging-{Guid.NewGuid():N}");

        WriteSlotFile(staging, content);

        var packed = await new SavegamePacker().PackAsync(Adapter, Target(staging), staging.Slot, CancellationToken.None);

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

    public string SlotPath(SavegameSlotRef slot) => Adapter.GetSlotPath(Target(slot), slot.Slot);

    /// <summary>The folder a slot reference addresses, the way the services resolve one.</summary>
    public SavegameTarget Target(SavegameSlotRef slot)
        => Adapter.SavegameTargets[slot.Target]
            ?? throw new InvalidOperationException($"This game reaches no savegame folder '{slot.Target}'.");

    /// <summary>What the packer says a slot holds now - the value an observation compares.</summary>
    public Task<string> HashSlotAsync(SavegameSlotRef slot)
        => new SavegamePacker().HashSlotAsync(Adapter, Target(slot), slot.Slot, CancellationToken.None);

    public void WriteSlotFile(SavegameSlotRef slot, string content)
    {
        var path = Path.Combine(SlotPath(slot), "careerSavegame.xml");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public string ReadSlotFile(SavegameSlotRef slot)
        => File.ReadAllText(Path.Combine(SlotPath(slot), "careerSavegame.xml"));

    public void Dispose()
    {
        _slots.Dispose();
        _clientSlots.Dispose();
        _manifests.Dispose();
        _store.Dispose();
    }
}
