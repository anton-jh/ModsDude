using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Tests.Sync;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>
/// The packer is filesystem and archive behaviour end to end, so - like the store's tests - it runs
/// against a real disk. A mocked one would agree with whatever the test assumed, and the properties
/// being asserted here are precisely the ones nobody can assume: the same content hash across two
/// runs over two differently-built folders, and an extractor that refuses to leave its own folder.
/// </summary>
public class SavegamePackerTests
{
    private static readonly SavegameSlotId _slot = new("savegame1");
    private static readonly SavegameSlotId _otherSlot = new("savegame2");


    [Fact]
    public async Task Packing_hands_the_caller_a_file_it_owns_and_the_hashes_of_what_it_holds()
    {
        using var root = new TempDirectory("savegame-pack");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "careerSavegame.xml", "a savegame");

        using var archive = await Pack(adapter, _slot);

        Assert.True(File.Exists(archive.FilePath));
        Assert.Equal(new FileInfo(archive.FilePath).Length, archive.Packed.SizeBytes);
        Assert.Equal(1, archive.Packed.EntryCount);

        // The archive hash is taken while the archive streams, so it must still be the hash of the
        // finished file - it is what an upload verifies.
        Assert.Equal(await HashOfFile(archive.FilePath), archive.Packed.ArchiveHash);

        // The content hash is the files', which is what the blob is addressed by.
        Assert.Equal(
            SavegameContentHash.Of([("careerSavegame.xml", HashOf("a savegame"))]),
            archive.ContentHash);
        Assert.Equal(archive.ContentHash, archive.Packed.SlotHash);
    }

    /// <summary>
    /// The property everything else rests on. If packing an unchanged save produced a new hash, every
    /// check-in would mint a snapshot for a night nobody played, and the drift check would report play
    /// in a slot that has been sitting still.
    /// </summary>
    [Fact]
    public async Task Packing_the_same_slot_twice_gives_the_same_content_hash()
    {
        using var root = new TempDirectory("savegame-deterministic");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "careerSavegame.xml", "a savegame");
        WriteSlotFile(root, _slot, "vehicles.xml", "two tractors");
        WriteSlotFile(root, _slot, "items/placeables.xml", "a shed");

        using var first = await Pack(adapter, _slot);
        using var second = await Pack(adapter, _slot);

        Assert.NotEqual(first.FilePath, second.FilePath);
        Assert.Equal(first.ContentHash, second.ContentHash);
    }

    /// <summary>
    /// The same save on two members' machines was written in whatever order each game happened to
    /// write it, and arrived with whatever timestamps the copy gave it. Neither is something anybody
    /// played, so neither may reach the hash.
    /// </summary>
    [Fact]
    public async Task Two_slots_built_in_different_orders_pack_identically()
    {
        using var root = new TempDirectory("savegame-order");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "vehicles.xml", "two tractors");
        WriteSlotFile(root, _slot, "items/placeables.xml", "a shed");
        WriteSlotFile(root, _slot, "careerSavegame.xml", "a savegame");

        WriteSlotFile(root, _otherSlot, "careerSavegame.xml", "a savegame");
        WriteSlotFile(root, _otherSlot, "items/placeables.xml", "a shed");
        WriteSlotFile(root, _otherSlot, "vehicles.xml", "two tractors");

        using var first = await Pack(adapter, _slot);
        using var second = await Pack(adapter, _otherSlot);

        Assert.Equal(first.ContentHash, second.ContentHash);
    }

    /// <summary>
    /// How a zip compresses is the runtime's business, so two clients - or one client after an
    /// update - may pack the same slot into different bytes. The contents are what is verified, so an
    /// archive compressed any other way still lands.
    /// </summary>
    [Fact]
    public async Task An_archive_compressed_differently_unpacks_as_the_same_contents()
    {
        using var root = new TempDirectory("savegame-compression");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "careerSavegame.xml", "a savegame");
        WriteSlotFile(root, _slot, "items/placeables.xml", "a shed");

        using var packed = await Pack(adapter, _slot);

        var stored = WriteArchiveWith(root, CompressionLevel.NoCompression, ("items/placeables.xml", "a shed"), ("careerSavegame.xml", "a savegame"));

        Assert.NotEqual(await HashOfFile(packed.FilePath), await HashOfFile(stored));

        await new SavegamePacker().UnpackAsync(stored, packed.ContentHash, adapter, adapter.Target, _otherSlot, CancellationToken.None);

        Assert.Equal(packed.ContentHash, await new SavegamePacker().HashSlotAsync(adapter, adapter.Target, _otherSlot, CancellationToken.None));
    }

    [Fact]
    public async Task Entries_are_ordinal_by_forward_slash_relative_path_and_carry_no_timestamp()
    {
        using var root = new TempDirectory("savegame-entries");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "vehicles.xml", "two tractors");
        WriteSlotFile(root, _slot, "items/deep/placeables.xml", "a shed");
        WriteSlotFile(root, _slot, "careerSavegame.xml", "a savegame");

        using var archive = await Pack(adapter, _slot);

        using var opened = ZipFile.OpenRead(archive.FilePath);

        Assert.Equal(
            ["careerSavegame.xml", "items/deep/placeables.xml", "vehicles.xml"],
            opened.Entries.Select(x => x.FullName));

        Assert.All(opened.Entries, x => Assert.Equal(new DateTime(1980, 1, 1), x.LastWriteTime.DateTime));
    }

    /// <summary>
    /// Rewriting a file with the same bytes is what an ordinary game launch does to half a save. It
    /// must read as nothing having happened.
    /// </summary>
    [Fact]
    public async Task Rewriting_a_file_unchanged_does_not_change_the_hash()
    {
        using var root = new TempDirectory("savegame-touch");
        var adapter = new PackerTestAdapter(root.Path);

        var path = WriteSlotFile(root, _slot, "careerSavegame.xml", "a savegame");

        var before = await new SavegamePacker().HashSlotAsync(adapter, adapter.Target, _slot, CancellationToken.None);

        File.WriteAllText(path, "a savegame");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(1));

        Assert.Equal(before, await new SavegamePacker().HashSlotAsync(adapter, adapter.Target, _slot, CancellationToken.None));
    }

    [Fact]
    public async Task Playing_changes_the_hash()
    {
        using var root = new TempDirectory("savegame-played");
        var adapter = new PackerTestAdapter(root.Path);

        var path = WriteSlotFile(root, _slot, "careerSavegame.xml", "a savegame");

        var before = await new SavegamePacker().HashSlotAsync(adapter, adapter.Target, _slot, CancellationToken.None);

        File.WriteAllText(path, "a savegame, two hours later");

        Assert.NotEqual(before, await new SavegamePacker().HashSlotAsync(adapter, adapter.Target, _slot, CancellationToken.None));
    }

    /// <summary>
    /// The drift check and the packer must never disagree about what a slot hashes to: one says the
    /// slot has been played and the other says the check-in is identical to the head, and only one of
    /// them can be right.
    /// </summary>
    [Fact]
    public async Task Hashing_a_slot_agrees_with_packing_it()
    {
        using var root = new TempDirectory("savegame-hash");
        var adapter = new PackerTestAdapter(root.Path, "screenshot.png");

        WriteSlotFile(root, _slot, "careerSavegame.xml", "a savegame");
        WriteSlotFile(root, _slot, "items/placeables.xml", "a shed");
        WriteSlotFile(root, _slot, "screenshot.png", "bulk that regenerates");

        using var archive = await Pack(adapter, _slot);
        var hashed = await new SavegamePacker().HashSlotAsync(adapter, adapter.Target, _slot, CancellationToken.None);

        Assert.Equal(archive.ContentHash, hashed);
    }

    [Fact]
    public async Task What_the_adapter_excludes_is_absent_from_the_archive_and_from_the_hash()
    {
        using var root = new TempDirectory("savegame-excluded");
        var adapter = new PackerTestAdapter(root.Path, "screenshot.png", "cache/thumbnail.png");

        WriteSlotFile(root, _slot, "careerSavegame.xml", "a savegame");
        WriteSlotFile(root, _slot, "screenshot.png", "bulk that regenerates");
        WriteSlotFile(root, _slot, "cache/thumbnail.png", "more of it");

        WriteSlotFile(root, _otherSlot, "careerSavegame.xml", "a savegame");

        using var withExclusions = await Pack(adapter, _slot);
        using var withoutTheFiles = await Pack(adapter, _otherSlot);

        using (var opened = ZipFile.OpenRead(withExclusions.FilePath))
        {
            Assert.Equal(["careerSavegame.xml"], opened.Entries.Select(x => x.FullName));
        }

        Assert.Equal(withoutTheFiles.ContentHash, withExclusions.ContentHash);

        Assert.Contains("cache/thumbnail.png", adapter.Asked);
        Assert.DoesNotContain(adapter.Asked, x => x.Contains('\\'));
    }

    /// <summary>
    /// The drift check runs against whatever is on disk, including a slot the user deleted from under
    /// it. An empty archive is an honest answer - a hash no played save shares - and its entry count
    /// is what lets a check-in refuse to upload nothing.
    /// </summary>
    [Fact]
    public async Task A_slot_that_is_not_there_packs_as_an_empty_archive_with_no_entries()
    {
        using var root = new TempDirectory("savegame-missing");
        var adapter = new PackerTestAdapter(root.Path);

        using var archive = await Pack(adapter, _slot);

        Assert.Equal(0, archive.Packed.EntryCount);

        using (var opened = ZipFile.OpenRead(archive.FilePath))
        {
            Assert.Empty(opened.Entries);
        }

        WriteSlotFile(root, _otherSlot, "careerSavegame.xml", "a savegame");
        using var played = await Pack(adapter, _otherSlot);

        Assert.NotEqual(played.ContentHash, archive.ContentHash);
    }

    /// <summary>
    /// A rename is packed into the archived copy and nowhere else, so a check-in the server refuses
    /// leaves the slot exactly as it was. The two hashes say what the slot holds and what the archive
    /// holds, and differ by the edit alone.
    /// </summary>
    [Fact]
    public async Task An_overlay_edits_the_archived_copy_and_leaves_the_slot_alone()
    {
        using var root = new TempDirectory("savegame-overlay");
        var adapter = new PackerTestAdapter(root.Path);

        var path = WriteSlotFile(root, _slot, "careerSavegame.xml", "named Old");
        WriteSlotFile(root, _slot, "vehicles.xml", "two tractors");

        var before = await new SavegamePacker().HashSlotAsync(adapter, adapter.Target, _slot, CancellationToken.None);

        using var archive = new OwnedArchive(await new SavegamePacker().PackAsync(
            adapter, adapter.Target, _slot, [Rename("careerSavegame.xml", "named New")], CancellationToken.None));

        Assert.Equal("named Old", await File.ReadAllTextAsync(path));
        Assert.Equal(before, archive.Packed.SlotHash);
        Assert.NotEqual(archive.Packed.SlotHash, archive.ContentHash);

        await new SavegamePacker().UnpackAsync(archive.FilePath, archive.ContentHash, adapter, adapter.Target, _otherSlot, CancellationToken.None);

        Assert.Equal("named New", await File.ReadAllTextAsync(Path.Combine(adapter.GetSlotPath(adapter.Target, _otherSlot), "careerSavegame.xml")));
        Assert.Equal(archive.ContentHash, await new SavegamePacker().HashSlotAsync(adapter, adapter.Target, _otherSlot, CancellationToken.None));
    }

    [Fact]
    public async Task An_overlay_edit_that_fails_packs_the_file_as_it_is()
    {
        using var root = new TempDirectory("savegame-overlay-fails");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "careerSavegame.xml", "named Old");

        using var archive = new OwnedArchive(await new SavegamePacker().PackAsync(
            adapter, adapter.Target, _slot,
            [new GameFileEdit("careerSavegame.xml", _ => throw new ArgumentException("Simulated: a name XML cannot hold."))],
            CancellationToken.None));

        Assert.Equal(archive.Packed.SlotHash, archive.ContentHash);
    }

    [Fact]
    public async Task An_overlay_edit_outside_the_slot_is_left_out_of_the_archive()
    {
        using var root = new TempDirectory("savegame-overlay-outside");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "careerSavegame.xml", "a savegame");

        using var archive = new OwnedArchive(await new SavegamePacker().PackAsync(
            adapter, adapter.Target, _slot, [Rename("../elsewhere.xml", "outside")], CancellationToken.None));

        using var opened = ZipFile.OpenRead(archive.FilePath);

        Assert.Equal(["careerSavegame.xml"], opened.Entries.Select(x => x.FullName));
        Assert.Equal(archive.Packed.SlotHash, archive.ContentHash);
    }

    [Fact]
    public async Task Unpacking_leaves_the_slot_holding_exactly_the_archive()
    {
        using var root = new TempDirectory("savegame-unpack");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "careerSavegame.xml", "a savegame");
        WriteSlotFile(root, _slot, "items/placeables.xml", "a shed");

        // Whatever was in the target is moved out, whether or not the archive has a file by that name.
        WriteSlotFile(root, _otherSlot, "careerSavegame.xml", "somebody else's savegame");
        WriteSlotFile(root, _otherSlot, "vehicles.xml", "and their tractors");

        using var archive = await Pack(adapter, _slot);
        var displaced = await new SavegamePacker().UnpackAsync(archive.FilePath, archive.ContentHash, adapter, adapter.Target, _otherSlot, CancellationToken.None);

        Assert.Equal(
            ["careerSavegame.xml", "items/placeables.xml"],
            RelativeContents(adapter.GetSlotPath(adapter.Target, _otherSlot)));

        // Handed back beside the slots under a name recovery recognises, never deleted.
        Assert.NotNull(displaced);
        Assert.StartsWith($".modsdude-replaced-{_otherSlot.Value}-", Path.GetFileName(displaced));
        Assert.Equal(["careerSavegame.xml", "vehicles.xml"], RelativeContents(displaced));

        // Round trip: what was checked out hashes as what was packed, which is what makes the
        // recorded hash of a fresh checkout mean "not played yet".
        Assert.Equal(archive.ContentHash, await new SavegamePacker().HashSlotAsync(adapter, adapter.Target, _otherSlot, CancellationToken.None));
    }

    [Fact]
    public async Task Unpacking_creates_a_slot_folder_that_does_not_exist_yet()
    {
        using var root = new TempDirectory("savegame-new-slot");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "careerSavegame.xml", "a savegame");

        using var archive = await Pack(adapter, _slot);
        var displaced = await new SavegamePacker().UnpackAsync(archive.FilePath, archive.ContentHash, adapter, adapter.Target, _otherSlot, CancellationToken.None);

        Assert.Equal(["careerSavegame.xml"], RelativeContents(adapter.GetSlotPath(adapter.Target, _otherSlot)));
        Assert.Null(displaced);
    }

    /// <summary>
    /// What arrived is checked against what was asked for as it is extracted, before the slot changes:
    /// a blob that is not the snapshot it was fetched as never reaches a slot.
    /// </summary>
    [Fact]
    public async Task Unpacking_contents_other_than_the_expected_ones_is_refused_and_leaves_the_slot_alone()
    {
        using var root = new TempDirectory("savegame-unpack-mismatch");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _otherSlot, "careerSavegame.xml", "the savegame that was already there");

        var archive = WriteArchiveWith(root, CompressionLevel.Optimal, ("careerSavegame.xml", "not what was asked for"));

        await Assert.ThrowsAsync<InvalidDataException>(() => new SavegamePacker().UnpackAsync(
            archive, SavegameContentHash.Of([("careerSavegame.xml", HashOf("what was asked for"))]), adapter, adapter.Target, _otherSlot, CancellationToken.None));

        Assert.Equal("the savegame that was already there", await File.ReadAllTextAsync(Path.Combine(adapter.GetSlotPath(adapter.Target, _otherSlot), "careerSavegame.xml")));
        Assert.Empty(Directory.EnumerateDirectories(root.Path, ".modsdude-*"));
    }

    /// <summary>
    /// An archive is bytes that came off a server, and a savegame slot sits inside the game's own
    /// data folder. An entry that names its way out of the slot is refused before anything is
    /// written, and refusing abandons the whole unpack.
    /// </summary>
    [Theory]
    [InlineData("../escaped.xml")]
    [InlineData("items/../../escaped.xml")]
    [InlineData("C:/escaped.xml")]
    public async Task Unpacking_refuses_an_entry_that_would_be_written_outside_the_slot(string entryName)
    {
        using var root = new TempDirectory("savegame-zip-slip");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "careerSavegame.xml", "the savegame that was already there");

        var hostile = WriteArchiveWith(root, CompressionLevel.Optimal, ("careerSavegame.xml", "a savegame"), (entryName, "somewhere else"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new SavegamePacker().UnpackAsync(hostile, "anything", adapter, adapter.Target, _slot, CancellationToken.None));

        Assert.False(File.Exists(root.Combine("escaped.xml")));
        Assert.False(File.Exists("C:\\escaped.xml"));

        // The slot is as it was, and nothing was left staged beside it.
        Assert.Equal("the savegame that was already there", await File.ReadAllTextAsync(Path.Combine(adapter.GetSlotPath(adapter.Target, _slot), "careerSavegame.xml")));
        Assert.Empty(Directory.EnumerateDirectories(root.Path, ".modsdude-*"));
    }

    /// <summary>
    /// Packing a folder that is hundreds of megabytes is minutes. What the strip can say is how much
    /// of the slot has been read, and saying it must not change what is packed.
    /// </summary>
    [Fact]
    public async Task Packing_reports_the_slots_bytes_without_changing_what_is_packed()
    {
        using var root = new TempDirectory("savegame-pack-progress");
        var adapter = new PackerTestAdapter(root.Path);

        // Past one report's worth, so there is a middle to look at and not only a start and an end.
        WriteSlotFile(root, _slot, "careerSavegame.xml", new string('a', 1_500_000));
        WriteSlotFile(root, _slot, "items/placeables.xml", "a shed");

        var reports = new List<SavegameProgress>();

        using var watched = new OwnedArchive(await new SavegamePacker().PackAsync(
            adapter, adapter.Target, _slot, [], CancellationToken.None, new InlineProgress<SavegameProgress>(reports.Add)));
        using var unwatched = await Pack(adapter, _slot);

        Assert.Equal(unwatched.ContentHash, watched.ContentHash);

        var total = 1_500_000L + "a shed".Length;

        Assert.All(reports, x => Assert.Equal(SavegameStage.Packing, x.Stage));
        Assert.All(reports, x => Assert.Equal(total, x.Total));

        Assert.Equal(0, reports.First().Completed);
        Assert.Equal(total, reports.Last().Completed);
        Assert.True(reports.Count > 2, "A file bigger than one report's worth should be reported part way through.");
        Assert.Equal(reports.Select(x => x.Completed).Order(), reports.Select(x => x.Completed));
    }

    [Fact]
    public async Task Unpacking_reports_the_bytes_it_has_written()
    {
        using var root = new TempDirectory("savegame-unpack-progress");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "careerSavegame.xml", new string('a', 1_500_000));

        using var archive = await Pack(adapter, _slot);

        var reports = new List<SavegameProgress>();

        await new SavegamePacker().UnpackAsync(
            archive.FilePath, archive.ContentHash, adapter, adapter.Target, _otherSlot, CancellationToken.None, new InlineProgress<SavegameProgress>(reports.Add));

        Assert.All(reports, x => Assert.Equal(SavegameStage.Unpacking, x.Stage));
        Assert.Equal(1_500_000L, reports.Last().Total);
        Assert.Equal(1_500_000L, reports.Last().Completed);
        Assert.True(reports.Count > 2);
    }


    private static async Task<OwnedArchive> Pack(PackerTestAdapter adapter, SavegameSlotId slot)
        => new(await new SavegamePacker().PackAsync(adapter, adapter.Target, slot, [], CancellationToken.None));

    private static GameFileEdit Rename(string relativePath, string content)
        => new(relativePath, _ => Encoding.UTF8.GetBytes(content));

    private static string HashOf(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static string WriteSlotFile(TempDirectory root, SavegameSlotId slot, string relativePath, string content)
        => root.WriteFile(Path.Combine(slot.Value, relativePath), content);

    /// <summary>Everything under a slot, forward-slashed and ordered, so an assertion can name it.</summary>
    private static IReadOnlyList<string> RelativeContents(string slotPath)
    {
        return
        [
            .. Directory.EnumerateFiles(slotPath, "*", SearchOption.AllDirectories)
                .Select(x => Path.GetRelativePath(slotPath, x).Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(x => x, StringComparer.Ordinal)
        ];
    }

    /// <summary>An archive built by hand, so an entry can be named or compressed in a way the packer never would.</summary>
    private static string WriteArchiveWith(TempDirectory root, CompressionLevel compression, params (string Name, string Content)[] entries)
    {
        var path = root.Combine($"archive-{Guid.NewGuid():N}.zip");

        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);

        foreach (var (name, content) in entries)
        {
            using var stream = archive.CreateEntry(name, compression).Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }

        return path;
    }

    private static async Task<string> HashOfFile(string path)
    {
        await using var content = File.OpenRead(path);

        return Convert.ToHexStringLower(await SHA256.HashDataAsync(content));
    }


    /// <summary>
    /// The packer only ever asks an adapter two things, and this answers both: where a slot's folder
    /// is, and what belongs in a packed save. It records what it was asked, because the form the
    /// question is put in - a forward-slashed relative path - is part of the contract.
    /// </summary>
    private sealed class PackerTestAdapter(string root, params string[] excluded) : ILocalSavegameAdapter
    {
        public List<string> Asked { get; } = [];

        /// <summary>One folder, because the packer is handed the one it is to pack.</summary>
        public SavegameTargets SavegameTargets => new(Target);

        public SavegameTarget Target { get; } = new(Keys.Target().Key, null, root);


        public string GetSlotPath(SavegameTarget target, SavegameSlotId slot) => Path.Combine(target.Path, slot.Value);

        public bool BelongsInPackedSave(string relativePath)
        {
            Asked.Add(relativePath);

            return excluded.Contains(relativePath, StringComparer.Ordinal) is false;
        }

        public Task<IReadOnlyList<SavegameSlot>> GetSlots(SavegameTarget target, CancellationToken cancellationToken)
            => throw new NotSupportedException("Packing addresses a slot it was given; it never enumerates them.");

        public ILocalSavegameAdapter WithLocalSettings(string serializedLocalSettings) => this;
        public ILocalSavegameAdapter WithLocalSettings(DynamicForm localSettings) => this;
    }


    /// <summary>
    /// A packed archive and the deletion the caller owes it. Every test that packs one uses this, so
    /// the ownership rule in <see cref="PackedSavegame.FilePath"/> is stated once in test code too.
    /// </summary>
    private sealed class OwnedArchive(PackedSavegame packed) : IDisposable
    {
        public PackedSavegame Packed { get; } = packed;

        public string FilePath => Packed.FilePath;
        public string ContentHash => Packed.ContentHash;


        public void Dispose()
        {
            try
            {
                File.Delete(Packed.FilePath);
            }
            catch (Exception)
            {
                // A leftover temp file is not worth failing a passing test over.
            }
        }
    }
}
