using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Storage;

public class BlobLabelTests
{
    private static readonly RepoId _repoId = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly DateTime _addedAt = new(2026, 11, 23, 12, 30, 0, DateTimeKind.Utc);


    [Fact]
    public void A_mod_label_names_the_repo_the_game_the_file_and_who_added_it()
    {
        var metadata = ModLabel().ApplyTo(new Dictionary<string, string>());

        Assert.Equal(
        [
            KeyValuePair.Create("reponame", "Gården"),
            KeyValuePair.Create("repoid", "11111111-1111-1111-1111-111111111111"),
            KeyValuePair.Create("game", "FarmingSimulator25"),
            KeyValuePair.Create("modid", "fs25_tractor"),
            KeyValuePair.Create("versionid", "1.0.0.0"),
            KeyValuePair.Create("displayname", "Traktor Ö"),
            KeyValuePair.Create("filename", "FS25_Tractor.zip"),
            KeyValuePair.Create("addedbyname", "Åsa"),
            KeyValuePair.Create("addedby", "user-1"),
            KeyValuePair.Create("addedat", "2026-11-23T12:30:00Z")
        ], BlobLabel.Read(metadata));
    }

    [Fact]
    public void Every_stored_value_is_ascii()
    {
        var metadata = ModLabel().ApplyTo(new Dictionary<string, string>());

        Assert.All(metadata.Values, x => Assert.All(x, c => Assert.True(c < 128, $"'{c}' in '{x}' is not ASCII")));
        Assert.Equal("G%C3%A5rden", metadata["reponame"]);
    }

    [Fact]
    public void A_savegame_label_names_the_savegame()
    {
        var label = new SavegameBlobLabel(
            _repoId, "Repo", new AdapterIdentifier("FarmingSimulator25"), new UserId("user-1"), "Anton", _addedAt,
            new SavegameId(Guid.Parse("33333333-3333-3333-3333-333333333333")), "Höstskörd");

        var read = BlobLabel.Read(label.ApplyTo(new Dictionary<string, string>())).ToDictionary();

        Assert.Equal("Höstskörd", read["savegamename"]);
        Assert.Equal("33333333-3333-3333-3333-333333333333", read["savegameid"]);
        Assert.DoesNotContain("modid", read.Keys);
    }

    [Fact]
    public void A_label_keeps_what_the_blob_carries_already()
    {
        var metadata = ModLabel().ApplyTo(new Dictionary<string, string> { ["sha256"] = "abc" });

        Assert.Equal("abc", metadata["sha256"]);
        Assert.Equal(KeyValuePair.Create("sha256", "abc"), BlobLabel.Read(metadata)[^1]);
    }

    [Fact]
    public void The_first_label_stays()
    {
        var first = ModLabel().ApplyTo(new Dictionary<string, string>());
        var second = (ModLabel() with { RepoName = "Renamed", AddedByName = "Someone else" }).ApplyTo(first);

        Assert.Equal(first.OrderBy(x => x.Key), second.OrderBy(x => x.Key));
    }

    [Fact]
    public void A_label_is_found_whatever_case_storage_returns_its_keys_in()
    {
        Assert.True(BlobLabel.IsPresentIn(new Dictionary<string, string> { ["RepoId"] = "x" }));
        Assert.False(BlobLabel.IsPresentIn(new Dictionary<string, string> { ["sha256"] = "x" }));
    }

    [Fact]
    public void A_mod_label_without_a_known_adder_leaves_the_adder_out()
    {
        var metadata = (ModLabel() with { AddedBy = null, AddedByName = null }).ApplyTo(new Dictionary<string, string>());

        Assert.DoesNotContain("addedby", metadata.Keys);
        Assert.DoesNotContain("addedbyname", metadata.Keys);
    }


    private static ModBlobLabel ModLabel()
    {
        return new ModBlobLabel(
            _repoId, "Gården", new AdapterIdentifier("FarmingSimulator25"), new UserId("user-1"), "Åsa", _addedAt,
            new ModId("fs25_tractor"), new ModVersionId("1.0.0.0"), "Traktor Ö", "FS25_Tractor.zip");
    }
}
