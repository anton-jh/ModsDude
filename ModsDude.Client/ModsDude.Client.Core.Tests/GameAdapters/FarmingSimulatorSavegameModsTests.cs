using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Tests.Sync;
using System.Xml;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.GameAdapters;

/// <summary>Which mods the reference adapter reads a save as played with, from career file entries shaped as real saves write them.</summary>
public class FarmingSimulatorSavegameModsTests : IDisposable
{
    private readonly TempDirectory _directory = new("fs-savegame-mods");


    public void Dispose() => _directory.Dispose();


    [Fact]
    public void Every_listed_mod_is_read_with_its_version_sorted_by_mod_id()
    {
        WriteCareer("""
            <careerSavegame revision="2" valid="true">
                <settings><savegameName>Farm</savegameName></settings>
                <mod modName="FS22_haySqueezer" title="Lizard Roadrunner Haysqueezer" version="1.0.0.1" required="false" fileHash="255051144fb5d9e2d0c494616e00cbb6"/>
                <mod modName="FS22_Courseplay" title="CoursePlay" version="7.4.0.0" required="false" fileHash="4d3340a7a8a4a93f74fa734a9c5304bb"/>
                <mod modName="FS22_The_Valley_The_Old_Farm" title="The Valley The Old Farm" version="1.0.0.1" required="true" fileHash="89a831f7b68b7f5abea65a9bec6b2409"/>
            </careerSavegame>
            """);

        var mods = Read();

        Assert.Equal(
            [
                new SavegameModReference(Mod("fs22_courseplay"), V("7.4.0.0")),
                new SavegameModReference(Mod("fs22_haysqueezer"), V("1.0.0.1")),
                new SavegameModReference(Mod("fs22_the_valley_the_old_farm"), V("1.0.0.1"))
            ],
            mods);
    }

    [Fact]
    public void Paid_dlcs_are_left_out()
    {
        WriteCareer("""
            <careerSavegame>
                <mod modName="pdlc_agiPack" title="AGI Pack" version="1.1.0.0" required="false"/>
                <mod modName="FS22_Courseplay" title="CoursePlay" version="7.4.0.0" required="false"/>
            </careerSavegame>
            """);

        Assert.Equal([Mod("fs22_courseplay")], Read().Select(x => x.ModId));
    }

    [Fact]
    public void A_mod_without_a_usable_version_is_listed_without_one()
    {
        WriteCareer("""
            <careerSavegame>
                <mod modName="FS22_NoVersion" title="No version"/>
                <mod modName="FS22_BlankVersion" title="Blank" version=" "/>
            </careerSavegame>
            """);

        Assert.All(Read(), x => Assert.Null(x.Version));
    }

    [Fact]
    public void An_entry_that_cannot_be_a_mod_id_is_left_out_and_the_rest_are_read()
    {
        WriteCareer("""
            <careerSavegame>
                <mod title="No name" version="1.0.0.0"/>
                <mod modName="has spaces / and slashes" version="1.0.0.0"/>
                <mod modName="FS22_Courseplay" version="7.4.0.0"/>
            </careerSavegame>
            """);

        Assert.Equal([Mod("fs22_courseplay")], Read().Select(x => x.ModId));
    }

    [Fact]
    public void A_mod_listed_twice_is_read_once_with_the_first_version()
    {
        WriteCareer("""
            <careerSavegame>
                <mod modName="FS22_Courseplay" version="7.4.0.0"/>
                <mod modName="fs22_courseplay" version="7.3.0.0"/>
            </careerSavegame>
            """);

        Assert.Equal([new SavegameModReference(Mod("fs22_courseplay"), V("7.4.0.0"))], Read());
    }

    [Fact]
    public void A_save_that_lists_no_mods_reads_as_none()
    {
        WriteCareer("<careerSavegame><settings /></careerSavegame>");

        Assert.Empty(Read());
    }

    /// <summary>A save is never shown as listing nothing because its file could not be read.</summary>
    [Fact]
    public void A_career_file_that_will_not_parse_throws()
    {
        WriteCareer("<careerSavegame><mod modName=");

        Assert.Throws<XmlException>(() => Read());
    }

    [Fact]
    public void A_missing_career_file_throws()
    {
        Assert.Throws<FileNotFoundException>(() => Read());
    }

    [Fact]
    public void Another_root_element_throws()
    {
        WriteCareer("<farms />");

        Assert.Throws<InvalidDataException>(() => Read());
    }

    [Fact]
    public async Task A_cancelled_read_stops()
    {
        WriteCareer("<careerSavegame />");

        var adapter = new FarmingSimulatorLocalSavegameAdapter(FarmingSimulatorGameVersion.Fs25, _directory.Path);
        var target = adapter.SavegameTargets.Single();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => adapter.ReadSavegameModsAsync(target, new SavegameSlotId("savegame1"), new CancellationToken(canceled: true)));
    }


    private IReadOnlyList<SavegameModReference> Read()
        => FarmingSimulatorLocalSavegameAdapter.ReadMods(_directory.Path, NullLogger.Instance, CancellationToken.None);

    private void WriteCareer(string xml)
        => File.WriteAllText(Path.Combine(_directory.Path, "careerSavegame.xml"), xml);
}
