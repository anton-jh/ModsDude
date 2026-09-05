using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
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
/// being asserted here are precisely the ones nobody can assume: byte-for-byte determinism across two
/// runs over two differently-built folders, and an extractor that refuses to leave its own folder.
/// </summary>
public class SavegamePackerTests
{
    private static readonly SavegameSlotId _slot = new("savegame1");
    private static readonly SavegameSlotId _otherSlot = new("savegame2");


    [Fact]
    public async Task Packing_hands_the_caller_a_file_it_owns_and_the_hash_of_that_file()
    {
        using var root = new TempDirectory("savegame-pack");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "careerSavegame.xml", "a farm");

        using var archive = await Pack(adapter, _slot);

        Assert.True(File.Exists(archive.FilePath));
        Assert.Equal(new FileInfo(archive.FilePath).Length, archive.Packed.SizeBytes);

        // The hash is taken while the archive streams, so the one thing that must be checked is that
        // it is still the hash of the finished file - it is what the blob will be addressed by.
        Assert.Equal(await HashOfFile(archive.FilePath), archive.ContentHash);
        Assert.Equal(archive.ContentHash, archive.ContentHash.ToLowerInvariant());
    }

    /// <summary>
    /// The property everything else rests on. If packing an unchanged save produced new bytes, every
    /// check-in would mint a version and a 400 MB blob for a night nobody played, and the drift check
    /// would report play in a slot that has been sitting still.
    /// </summary>
    [Fact]
    public async Task Packing_the_same_slot_twice_produces_the_same_bytes()
    {
        using var root = new TempDirectory("savegame-deterministic");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "careerSavegame.xml", "a farm");
        WriteSlotFile(root, _slot, "vehicles.xml", "two tractors");
        WriteSlotFile(root, _slot, "items/placeables.xml", "a shed");

        using var first = await Pack(adapter, _slot);
        using var second = await Pack(adapter, _slot);

        Assert.NotEqual(first.FilePath, second.FilePath);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(await File.ReadAllBytesAsync(first.FilePath), await File.ReadAllBytesAsync(second.FilePath));
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
        WriteSlotFile(root, _slot, "careerSavegame.xml", "a farm");

        WriteSlotFile(root, _otherSlot, "careerSavegame.xml", "a farm");
        WriteSlotFile(root, _otherSlot, "items/placeables.xml", "a shed");
        WriteSlotFile(root, _otherSlot, "vehicles.xml", "two tractors");

        using var first = await Pack(adapter, _slot);
        using var second = await Pack(adapter, _otherSlot);

        Assert.Equal(first.ContentHash, second.ContentHash);
    }

    [Fact]
    public async Task Entries_are_ordinal_by_forward_slash_relative_path_and_carry_no_timestamp()
    {
        using var root = new TempDirectory("savegame-entries");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "vehicles.xml", "two tractors");
        WriteSlotFile(root, _slot, "items/deep/placeables.xml", "a shed");
        WriteSlotFile(root, _slot, "careerSavegame.xml", "a farm");

        using var archive = await Pack(adapter, _slot);

        using var opened = ZipFile.OpenRead(archive.FilePath);

        Assert.Equal(
            ["careerSavegame.xml", "items/deep/placeables.xml", "vehicles.xml"],
            opened.Entries.Select(x => x.FullName));

        // Pinned rather than the file's own, which is the other half of determinism: copying a save
        // between machines rewrites every mtime while changing nothing that was played. Compared as
        // a wall clock, because that is all a zip stores - the offset that comes back out is the
        // reader's own, which is exactly why the value written must not depend on the writer's.
        Assert.All(opened.Entries, x => Assert.Equal(new DateTime(1980, 1, 1), x.LastWriteTime.DateTime));
    }

    /// <summary>
    /// Rewriting a file with the same bytes is what an ordinary game launch does to half a save. It
    /// must read as nothing having happened, or "you played for 2 hours, check it in" becomes a
    /// notification everybody learns to click past.
    /// </summary>
    [Fact]
    public async Task Rewriting_a_file_unchanged_does_not_change_the_hash()
    {
        using var root = new TempDirectory("savegame-touch");
        var adapter = new PackerTestAdapter(root.Path);

        var path = WriteSlotFile(root, _slot, "careerSavegame.xml", "a farm");

        var before = await new SavegamePacker().HashSlotAsync(adapter, _slot, CancellationToken.None);

        File.WriteAllText(path, "a farm");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(1));

        Assert.Equal(before, await new SavegamePacker().HashSlotAsync(adapter, _slot, CancellationToken.None));
    }

    [Fact]
    public async Task Playing_changes_the_hash()
    {
        using var root = new TempDirectory("savegame-played");
        var adapter = new PackerTestAdapter(root.Path);

        var path = WriteSlotFile(root, _slot, "careerSavegame.xml", "a farm");

        var before = await new SavegamePacker().HashSlotAsync(adapter, _slot, CancellationToken.None);

        File.WriteAllText(path, "a farm, two hours later");

        Assert.NotEqual(before, await new SavegamePacker().HashSlotAsync(adapter, _slot, CancellationToken.None));
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

        WriteSlotFile(root, _slot, "careerSavegame.xml", "a farm");
        WriteSlotFile(root, _slot, "items/placeables.xml", "a shed");
        WriteSlotFile(root, _slot, "screenshot.png", "bulk that regenerates");

        using var archive = await Pack(adapter, _slot);
        var hashed = await new SavegamePacker().HashSlotAsync(adapter, _slot, CancellationToken.None);

        Assert.Equal(archive.ContentHash, hashed);
    }

    [Fact]
    public async Task What_the_adapter_excludes_is_absent_from_the_archive_and_from_the_hash()
    {
        using var root = new TempDirectory("savegame-excluded");
        var adapter = new PackerTestAdapter(root.Path, "screenshot.png", "cache/thumbnail.png");

        WriteSlotFile(root, _slot, "careerSavegame.xml", "a farm");
        WriteSlotFile(root, _slot, "screenshot.png", "bulk that regenerates");
        WriteSlotFile(root, _slot, "cache/thumbnail.png", "more of it");

        WriteSlotFile(root, _otherSlot, "careerSavegame.xml", "a farm");

        using var withExclusions = await Pack(adapter, _slot);
        using var withoutTheFiles = await Pack(adapter, _otherSlot);

        using (var opened = ZipFile.OpenRead(withExclusions.FilePath))
        {
            Assert.Equal(["careerSavegame.xml"], opened.Entries.Select(x => x.FullName));
        }

        // Excluded is excluded, not merely skipped: a save whose screenshot changed hashes the same
        // as one that never had a screenshot, so a regenerated thumbnail can never read as play.
        Assert.Equal(withoutTheFiles.ContentHash, withExclusions.ContentHash);

        // And the adapter is asked in the form the archive stores, so an exclusion rule written
        // against a path rather than a file name works on every platform.
        Assert.Contains("cache/thumbnail.png", adapter.Asked);
        Assert.DoesNotContain(adapter.Asked, x => x.Contains('\\'));
    }

    /// <summary>
    /// The drift check runs on window activation against whatever is on disk, including a slot the
    /// user deleted from under it. An empty archive is an honest answer - a real hash no played save
    /// shares - where an exception would be a crash on a path nobody asked for.
    /// </summary>
    [Fact]
    public async Task A_slot_that_is_not_there_packs_as_an_empty_archive()
    {
        using var root = new TempDirectory("savegame-missing");
        var adapter = new PackerTestAdapter(root.Path);

        using var archive = await Pack(adapter, _slot);

        using (var opened = ZipFile.OpenRead(archive.FilePath))
        {
            Assert.Empty(opened.Entries);
        }

        WriteSlotFile(root, _otherSlot, "careerSavegame.xml", "a farm");
        using var played = await Pack(adapter, _otherSlot);

        Assert.NotEqual(played.ContentHash, archive.ContentHash);
    }

    [Fact]
    public async Task Unpacking_leaves_the_slot_holding_exactly_the_archive()
    {
        using var root = new TempDirectory("savegame-unpack");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "careerSavegame.xml", "a farm");
        WriteSlotFile(root, _slot, "items/placeables.xml", "a shed");

        // Whatever was in the target is gone, whether or not the archive has a file by that name.
        WriteSlotFile(root, _otherSlot, "careerSavegame.xml", "somebody else's farm");
        WriteSlotFile(root, _otherSlot, "vehicles.xml", "and their tractors");

        using var archive = await Pack(adapter, _slot);
        await new SavegamePacker().UnpackAsync(archive.FilePath, adapter, _otherSlot, CancellationToken.None);

        Assert.Equal(
            ["careerSavegame.xml", "items/placeables.xml"],
            RelativeContents(adapter.GetSlotPath(_otherSlot)));

        Assert.Equal("a farm", await File.ReadAllTextAsync(Path.Combine(adapter.GetSlotPath(_otherSlot), "careerSavegame.xml")));

        // Round trip: what was checked out hashes as what was packed, which is what makes the
        // recorded hash of a fresh checkout mean "not played yet".
        Assert.Equal(archive.ContentHash, await new SavegamePacker().HashSlotAsync(adapter, _otherSlot, CancellationToken.None));
    }

    [Fact]
    public async Task Unpacking_creates_a_slot_folder_that_does_not_exist_yet()
    {
        using var root = new TempDirectory("savegame-new-slot");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "careerSavegame.xml", "a farm");

        using var archive = await Pack(adapter, _slot);
        await new SavegamePacker().UnpackAsync(archive.FilePath, adapter, _otherSlot, CancellationToken.None);

        Assert.Equal(["careerSavegame.xml"], RelativeContents(adapter.GetSlotPath(_otherSlot)));
    }

    /// <summary>
    /// An archive is bytes that came off a server, and a savegame slot sits inside the game's own
    /// data folder - beside the other nineteen slots, and often beside the mod folder. An entry that
    /// names its way out of the slot is refused before anything is written, and refusing abandons the
    /// whole unpack: half an archive on disk is not a state anybody can reason about.
    /// </summary>
    [Theory]
    [InlineData("../escaped.xml")]
    [InlineData("items/../../escaped.xml")]
    [InlineData("C:/escaped.xml")]
    public async Task Unpacking_refuses_an_entry_that_would_be_written_outside_the_slot(string entryName)
    {
        using var root = new TempDirectory("savegame-zip-slip");
        var adapter = new PackerTestAdapter(root.Path);

        WriteSlotFile(root, _slot, "careerSavegame.xml", "the farm that was already there");

        var hostile = WriteArchiveWith(root, ("careerSavegame.xml", "a farm"), (entryName, "somewhere else"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new SavegamePacker().UnpackAsync(hostile, adapter, _slot, CancellationToken.None));

        Assert.False(File.Exists(root.Combine("escaped.xml")));
        Assert.False(File.Exists("C:\\escaped.xml"));

        // The slot is as it was, and nothing was left staged beside it.
        Assert.Equal("the farm that was already there", await File.ReadAllTextAsync(Path.Combine(adapter.GetSlotPath(_slot), "careerSavegame.xml")));
        Assert.Empty(Directory.EnumerateDirectories(root.Path, ".modsdude-*"));
    }


    private static async Task<OwnedArchive> Pack(IInstanceSavegameAdapter adapter, SavegameSlotId slot)
        => new(await new SavegamePacker().PackAsync(adapter, slot, CancellationToken.None));

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

    /// <summary>An archive built by hand, so an entry can be named something the packer would never write.</summary>
    private static string WriteArchiveWith(TempDirectory root, params (string Name, string Content)[] entries)
    {
        var path = root.Combine($"archive-{Guid.NewGuid():N}.zip");

        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);

        foreach (var (name, content) in entries)
        {
            using var stream = archive.CreateEntry(name).Open();
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
    private sealed class PackerTestAdapter(string root, params string[] excluded) : IInstanceSavegameAdapter
    {
        public List<string> Asked { get; } = [];

        public bool CanCreateSlots => true;


        public string GetSlotPath(SavegameSlotId slot) => Path.Combine(root, slot.Value);

        public bool BelongsInPackedSave(string relativePath)
        {
            Asked.Add(relativePath);

            return excluded.Contains(relativePath, StringComparer.Ordinal) is false;
        }

        public Task<IReadOnlyList<SavegameSlot>> GetSlots(CancellationToken cancellationToken)
            => throw new NotSupportedException("Packing addresses a slot it was given; it never enumerates them.");

        public IInstanceSavegameAdapter WithInstanceSettings(string serializedInstanceSettings) => this;
        public IInstanceSavegameAdapter WithInstanceSettings(DynamicForm instanceSettings) => this;
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
