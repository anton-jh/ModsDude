using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
using ModsDude.Client.Core.GameFiles;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Tests.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModsDude.Client.Core.Tests.GameAdapters;

/// <summary>
/// Writing a name into a slot's own career file - the local half of "the game's own menu and the repo
/// agree on what a save is called". Against the same real FS25 layout
/// <see cref="FarmingSimulatorSavegameDetailTests"/> reads from, because getting the element right is
/// only half the job when the other half is writing back into it without corrupting the file the
/// game itself owns.
/// </summary>
public class FarmingSimulatorSavegameRenameTests : IDisposable
{
    private readonly TempDirectory _gameData = new("fs-savegame-rename");


    public void Dispose() => _gameData.Dispose();


    [Fact]
    public void Renaming_writes_the_new_name_into_the_career_file()
    {
        WriteCareer(RealCareerFile);

        var changed = Rename("Season 5");

        Assert.True(changed);
        Assert.Equal("Season 5", ReadBack().DisplayName);
    }

    /// <summary>
    /// The no-op case, and asserted the strong way: not just that the name reads back the same, but
    /// that the file was never opened for writing at all. A rewrite that happened to reproduce the same
    /// value would still be a rewrite - reformatted whitespace, a changed BOM - and the whole point of
    /// checking first is that nothing on disk moves when there is nothing to change.
    /// </summary>
    [Fact]
    public void Renaming_to_the_name_already_there_changes_nothing_on_disk()
    {
        WriteCareer(RealCareerFile);
        var before = File.ReadAllBytes(CareerFilePath);

        var changed = Rename("My game save");

        Assert.False(changed);
        Assert.Equal(before, File.ReadAllBytes(CareerFilePath));
    }

    [Theory]
    [InlineData("O'Brien's Farm")]
    [InlineData("Dudes & Co <2>")]
    [InlineData("\"Quoted\"")]
    public void Renaming_round_trips_characters_xml_has_to_escape(string name)
    {
        WriteCareer(RealCareerFile);

        Assert.True(Rename(name));
        Assert.Equal(name, ReadBack().DisplayName);
    }

    [Fact]
    public void Renaming_a_slot_with_no_career_file_does_nothing()
    {
        var changed = Rename("Season 5");

        Assert.False(changed);
        Assert.False(File.Exists(CareerFilePath));
    }

    /// <summary>
    /// The same degrade-rather-than-throw the reader gives a career file a running game is mid-write
    /// on: <see cref="FarmingSimulatorSavegameDetailTests.A_career_file_that_will_not_parse_is_occupied_and_unnamed"/>
    /// is the read side of this.
    /// </summary>
    [Fact]
    public void Renaming_a_career_file_that_will_not_parse_does_nothing()
    {
        WriteCareer("<careerSavegame><settings>");
        var before = File.ReadAllText(CareerFilePath);

        var changed = Rename("Season 5");

        Assert.False(changed);
        Assert.Equal(before, File.ReadAllText(CareerFilePath));
    }

    [Fact]
    public void Renaming_a_career_file_with_no_savegameName_element_does_nothing()
    {
        WriteCareer("""
            <careerSavegame>
                <settings>
                    <mapTitle>Zielonka</mapTitle>
                </settings>
            </careerSavegame>
            """);

        Assert.False(Rename("Season 5"));
    }

    /// <summary>
    /// The declaration is part of what the game wrote, and a rewrite that dropped it - or its
    /// <c>standalone="no"</c> - would be a bigger edit than the one name element this is meant to touch.
    /// </summary>
    [Fact]
    public void Renaming_preserves_the_xml_declaration()
    {
        WriteCareer(RealCareerFile);

        Rename("Season 5");

        var written = File.ReadAllText(CareerFilePath);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"no\"?>", written);
    }

    /// <summary>Every other field survives a rename untouched - this writes one element, not the file.</summary>
    [Fact]
    public void Renaming_leaves_every_other_field_as_it_was()
    {
        WriteCareer(RealCareerFile);

        Rename("Season 5");

        var slot = ReadBack();

        Assert.Contains(slot.Details, x => x.Id == SavegameDetail.Ids.Map && x.Value == "Zielonka");
        Assert.Contains(slot.Details, x => x.Id == SavegameDetail.Ids.Difficulty && x.Value == "Normal");
    }


    /// <summary>
    /// The file differs from what the game wrote by the name alone: same indentation, same whitespace,
    /// same line endings. A rename that reformatted the file would be an edit the game never asked for.
    /// </summary>
    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void Renaming_changes_the_name_and_nothing_else_in_the_file(string newLine)
    {
        var original = RealCareerFile.ReplaceLineEndings(newLine);
        WriteCareer(original);

        Rename("Season 5");

        Assert.Equal(original.Replace("My game save", "Season 5"), File.ReadAllText(CareerFilePath));
    }

    /// <summary>
    /// A name XML cannot hold is left out rather than thrown on: the server refuses such names, and a
    /// rename is decoration next to the bytes.
    /// </summary>
    /// <remarks>
    /// The names are built in code rather than passed as theory data, which the test runner re-encodes:
    /// a lone surrogate would arrive as a replacement character, which XML holds perfectly well.
    /// </remarks>
    [Fact]
    public void Renaming_to_a_name_xml_cannot_hold_leaves_the_file_alone()
    {
        WriteCareer(RealCareerFile);
        var before = File.ReadAllBytes(CareerFilePath);

        Assert.False(Rename("Season" + (char)0 + "5"));
        Assert.False(Rename("Season " + (char)0xD83C));
        Assert.Equal(before, File.ReadAllBytes(CareerFilePath));
    }

    /// <summary>The career file a rename replaces is the game's own file, so it goes to the Recycle Bin.</summary>
    [Fact]
    public void The_career_file_a_rename_replaces_goes_to_the_recycle_bin()
    {
        var edit = Assert.Single(Adapter().RenameSavegame(new SavegameRenameContext(Target(), Slot, "Season 5")));

        Assert.True(edit.RecycleReplaced);
    }

    [Fact]
    public void Renaming_twice_changes_nothing_the_second_time()
    {
        WriteCareer(RealCareerFile);
        Rename("Season 5");
        var after = File.ReadAllBytes(CareerFilePath);

        Assert.False(Rename("Season 5"));
        Assert.Equal(after, File.ReadAllBytes(CareerFilePath));
    }


    private static readonly SavegameSlotId Slot = new("savegame1");

    /// <summary>The adapter's edits, carried out the way the engine carries them out.</summary>
    private bool Rename(string name)
    {
        var adapter = Adapter();
        var target = Target();
        var editor = new GameFileEditor(new FakeRecycleBin(), NullLogger<GameFileEditor>.Instance);

        return adapter.RenameSavegame(new SavegameRenameContext(target, Slot, name))
            .Select(x => editor.Apply(adapter.GetSlotPath(target, Slot), x))
            .ToList()
            .Any(x => x);
    }

    private string CareerFilePath => Path.Combine(_gameData.Path, Slot.Value, "careerSavegame.xml");

    private FarmingSimulatorLocalSavegameAdapter Adapter()
        => new(FarmingSimulatorGameVersion.Fs25, _gameData.Path);

    private SavegameTarget Target() => Adapter().SavegameTargets.Single();

    private SavegameSlot ReadBack()
        => FarmingSimulatorLocalSavegameAdapter.ReadSlot(Slot, Path.Combine(_gameData.Path, Slot.Value), NullLogger.Instance, CancellationToken.None);

    private void WriteCareer(string xml)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CareerFilePath)!);
        File.WriteAllText(CareerFilePath, xml);
    }

    /// <summary>The shape a real FS25 save has - the same constant <see cref="FarmingSimulatorSavegameDetailTests"/> uses.</summary>
    private const string RealCareerFile = """
        <?xml version="1.0" encoding="utf-8" standalone="no"?>
        <careerSavegame revision="2" valid="true">
            <settings>
                <savegameName>My game save</savegameName>
                <creationDate>2024-11-12</creationDate>
                <mapId>MapEU</mapId>
                <mapTitle>Zielonka</mapTitle>
                <saveDateFormatted>2024-11-30</saveDateFormatted>
                <saveDate>2024-11-30</saveDate>
                <economicDifficulty>NORMAL</economicDifficulty>
            </settings>
            <statistics>
                <money>284067</money>
                <playTime>2717.009521</playTime>
            </statistics>
        </careerSavegame>
        """;
}
