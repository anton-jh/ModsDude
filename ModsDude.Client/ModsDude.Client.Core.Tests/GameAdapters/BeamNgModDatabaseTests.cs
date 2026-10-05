using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;
using System.Text;
using System.Text.Json.Nodes;

namespace ModsDude.Client.Core.Tests.GameAdapters;

public class BeamNgModDatabaseTests
{
    private const string _database = """
        {
          "header": { "version": 1.1 },
          "mods": {
            "roamup": {
              "active": false,
              "fullpath": "/mods/Roamup.zip",
              "modData": { "rating_avg": 4.94737, "title": "Gavril Roamer Pickup" }
            },
            "extragriptires": {
              "active": false,
              "fullpath": "/mods/repo/EXTRAGRIPTIRES.zip"
            },
            "mine": {
              "active": false,
              "fullpath": "/mods/mine.zip"
            }
          }
        }
        """;


    [Fact]
    public void The_profiles_mods_are_switched_on_and_nothing_else()
    {
        var result = Parse(Activate(_database, "roamup.zip"));

        Assert.True(result["mods"]!["roamup"]!["active"]!.GetValue<bool>());
        Assert.False(result["mods"]!["extragriptires"]!["active"]!.GetValue<bool>());
        Assert.False(result["mods"]!["mine"]!["active"]!.GetValue<bool>());
    }

    [Fact]
    public void Everything_the_edit_does_not_manage_is_kept()
    {
        var result = Parse(Activate(_database, "Roamup.zip"));

        Assert.Equal(1.1, result["header"]!["version"]!.GetValue<double>());
        Assert.Equal("Gavril Roamer Pickup", result["mods"]!["roamup"]!["modData"]!["title"]!.GetValue<string>());
        Assert.Equal("4.94737", result["mods"]!["roamup"]!["modData"]!["rating_avg"]!.ToJsonString());
    }

    [Fact]
    public void Applying_it_again_changes_nothing()
    {
        var once = Activate(_database, "Roamup.zip");

        Assert.Null(BeamNgModDatabase.Activate(["Roamup.zip"]).Transform(Encoding.UTF8.GetBytes(once)));
    }

    [Fact]
    public void A_mod_in_a_subfolder_is_not_the_profiles()
    {
        Assert.Null(BeamNgModDatabase.Activate(["EXTRAGRIPTIRES.zip"]).Transform(Encoding.UTF8.GetBytes(_database)));
    }

    [Fact]
    public void A_mod_the_game_has_not_listed_yet_is_left_to_the_game()
    {
        Assert.Null(BeamNgModDatabase.Activate(["new.zip"]).Transform(Encoding.UTF8.GetBytes(_database)));
    }

    [Fact]
    public void No_list_yet_is_nothing_to_change()
    {
        Assert.Null(BeamNgModDatabase.Activate(["Roamup.zip"]).Transform(null));
    }

    [Fact]
    public void A_list_that_is_not_json_refuses_the_edit()
    {
        var edit = BeamNgModDatabase.Activate(["Roamup.zip"]);

        Assert.Throws<UserFriendlyException>(() => edit.Transform(Encoding.UTF8.GetBytes("{ half written")));
    }

    [Fact]
    public void Replaced_content_goes_to_the_recycle_bin()
    {
        Assert.True(BeamNgModDatabase.Activate([]).RecycleReplaced);
    }


    private static string Activate(string database, params string[] placed)
    {
        var result = BeamNgModDatabase.Activate(placed).Transform(Encoding.UTF8.GetBytes(database));

        return Encoding.UTF8.GetString(Assert.IsType<byte[]>(result));
    }

    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;
}
