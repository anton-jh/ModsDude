using ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Tests.Sync;
using System.IO.Compression;
using System.Text;

namespace ModsDude.Client.Core.Tests.GameAdapters;

/// <summary>
/// The adapter is exercised through a real folder of real archives, because what it accepts, what it
/// skips and what it reads are decided by the zip's contents.
/// </summary>
public class BeamNgModArchiveTests : IDisposable
{
    private static readonly DateTimeOffset _packedAt = new(new DateTime(2024, 8, 9, 13, 45, 0));

    private readonly TempDirectory _folder = new("beamng-archives");


    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _folder.Dispose();
    }


    [Fact]
    public async Task A_repository_mod_is_read_from_its_info_json()
    {
        WriteZip("Roamup.zip", new()
        {
            ["mod_info/MAUT1EJZL/info.json"] = """
                {
                    "title": "Gavril Roamer Pickup",
                    "version_string": "1.3",
                    "username": "JustRobert",
                    "tag_line": "Utility Roamer",
                    "message": "[CENTER]Ever wanted a [B]Roamer[/B]?[/CENTER]\n[MEDIA=youtube]abc[/MEDIA]"
                }
                """,
            ["mod_info/MAUT1EJZL/icon.jpg"] = "icon",
            ["mod_info/MAUT1EJZL/images/2.png"] = "second",
            ["mod_info/MAUT1EJZL/images/1.png"] = "first",
            ["vehicles/roamer/roamup.jbeam"] = "{}"
        });

        var mod = await ScanOne();

        Assert.Equal(ModKey.From("roamup"), mod.Id);
        Assert.Equal("1.3", mod.Version.Value);
        Assert.Equal("Gavril Roamer Pickup", mod.Name);
        Assert.Equal("JustRobert", mod.Author);
        Assert.Equal("Ever wanted a Roamer?", mod.Description);
        Assert.Equal("icon.jpg", mod.Icon?.Name);
        Assert.Equal(["1.png", "2.png"], mod.Images.Select(x => x.Name));
        Assert.Equal("Roamup.zip", mod.FileName.Value);
    }

    [Fact]
    public async Task A_mod_without_info_json_is_named_after_its_file_and_versioned_by_its_newest_file()
    {
        WriteZip("my_truck.zip", new()
        {
            ["vehicles/my_truck/default.png"] = "preview",
            ["vehicles/my_truck/my_truck.jbeam"] = "{}"
        });

        var mod = await ScanOne();

        Assert.Equal("my_truck", mod.Name);
        Assert.Equal("2024.08.09.1345", mod.Version.Value);
        Assert.Equal("", mod.Description);
        Assert.Null(mod.Author);
        Assert.Equal("default.png", mod.Icon?.Name);
    }

    [Fact]
    public async Task The_tag_line_describes_a_mod_whose_message_is_empty()
    {
        WriteZip("a.zip", new()
        {
            ["mod_info/X/info.json"] = """{ "tag_line": "Short and sweet", "message": "" }""",
            ["lua/ge/extensions/a.lua"] = ""
        });

        Assert.Equal("Short and sweet", (await ScanOne()).Description);
    }

    [Fact]
    public async Task An_unreadable_info_json_reads_the_mod_without_it()
    {
        WriteZip("broken_info.zip", new()
        {
            ["mod_info/X/info.json"] = "{ not json",
            ["vehicles/x/x.jbeam"] = "{}"
        });

        var mod = await ScanOne();

        Assert.Equal("broken_info", mod.Name);
        Assert.Equal("2024.08.09.1345", mod.Version.Value);
    }

    [Fact]
    public async Task A_level_preview_is_the_picture_of_a_map()
    {
        WriteZip("utah.zip", new()
        {
            ["levels/utah/utah_preview.jpg"] = "preview",
            ["levels/utah/main.level.json"] = "{}"
        });

        var mod = await ScanOne();

        Assert.Equal("utah_preview.jpg", mod.Icon?.Name);
        Assert.Equal(["utah_preview.jpg"], mod.Images.Select(x => x.Name));
    }

    [Fact]
    public async Task The_type_of_content_is_read_off_the_folders()
    {
        WriteZip("pack.zip", new()
        {
            ["vehicles/a/a.jbeam"] = "{}",
            ["levels/b/main.level.json"] = "{}",
            ["lua/vehicle/c.lua"] = "",
            ["scripts/d/modScript.lua"] = ""
        });

        var mod = await ScanOne();

        Assert.Equal(
            [new ModAttribute("type", "vehicle"), new ModAttribute("type", "map"), new ModAttribute("type", "script")],
            mod.Attributes);
    }

    [Fact]
    public async Task A_zip_holding_no_game_content_is_not_a_mod()
    {
        WriteZip("photos.zip", new() { ["holiday/beach.jpg"] = "sand" });

        Assert.Empty(await Scan());
    }

    [Fact]
    public async Task A_damaged_archive_is_skipped_and_the_rest_still_read()
    {
        File.WriteAllText(_folder.Combine("broken.zip"), "not a zip at all");
        WriteZip("fine.zip", new() { ["vehicles/a/a.jbeam"] = "{}" });

        var mod = Assert.Single(await Scan());

        Assert.Equal("fine", mod.Name);
    }

    [Fact]
    public async Task Files_that_are_not_archives_are_never_candidates()
    {
        File.WriteAllText(_folder.Combine("db.json"), "{}");

        Assert.Empty(await Scan());
    }


    private void WriteZip(string name, Dictionary<string, string> entries)
    {
        using var zip = ZipFile.Open(_folder.Combine(name), ZipArchiveMode.Create);

        foreach (var (path, content) in entries)
        {
            var entry = zip.CreateEntry(path);
            entry.LastWriteTime = _packedAt;

            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }
    }

    private Task<IEnumerable<LocalMod>> Scan()
        => new BeamNgBaseModAdapter().GetModsFromFolder(_folder.Path, CancellationToken.None);

    private async Task<LocalMod> ScanOne() => Assert.Single(await Scan());
}
