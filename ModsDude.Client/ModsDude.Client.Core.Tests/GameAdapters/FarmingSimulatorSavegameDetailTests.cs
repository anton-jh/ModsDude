using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.GameAdapters;

/// <summary>
/// What the reference adapter reads out of a save, against the layout a real Farming Simulator 25
/// save actually has.
/// </summary>
/// <remarks>
/// Written from a real <c>careerSavegame.xml</c> and <c>farms.xml</c>, because the element names are
/// the whole of what this code knows and guessing at them is how playtime came to be read from the
/// wrong parent for as long as it was.
/// </remarks>
public class FarmingSimulatorSavegameDetailTests : IDisposable
{
    private readonly TempDirectory _directory = new("fs-savegame");


    public void Dispose() => _directory.Dispose();


    [Fact]
    public void An_empty_slot_says_so_and_describes_nothing()
    {
        var slot = Read();

        Assert.False(slot.IsOccupied);
        Assert.Null(slot.DisplayName);
        Assert.Empty(slot.Details);
    }

    /// <summary>
    /// The one mistake that matters: an occupied slot reported as empty is the one the engine writes
    /// into without asking.
    /// </summary>
    [Fact]
    public void A_career_file_that_will_not_parse_is_occupied_and_unnamed()
    {
        WriteCareer("<careerSavegame><settings>");

        var slot = Read();

        Assert.True(slot.IsOccupied);
        Assert.Null(slot.DisplayName);
        Assert.Empty(slot.Details);
    }

    [Fact]
    public void The_save_is_named_the_way_the_game_names_it()
    {
        WriteCareer(RealCareerFile);

        Assert.Equal("My game save", Read().DisplayName);
    }

    [Fact]
    public void The_map_is_the_title_rather_than_the_id()
    {
        WriteCareer(RealCareerFile);

        Assert.Equal("Zielonka", Detail(SavegameDetail.Ids.Map)?.Value);
        Assert.Equal("Map", Detail(SavegameDetail.Ids.Map)?.Label);
    }

    /// <summary>
    /// The regression this all started from: playTime lives under &lt;statistics&gt;, and reading it
    /// from &lt;settings&gt; meant every slot silently reported no playtime at all.
    /// </summary>
    [Fact]
    public void Playtime_is_read_from_statistics_and_not_from_settings()
    {
        WriteCareer(RealCareerFile);

        // 2717 minutes, which nobody wants to read as "1.21:17:00".
        Assert.Equal("45 h", Detail(SavegameDetail.Ids.Playtime)?.Value);
    }

    [Fact]
    public void Short_playtimes_stay_in_minutes()
    {
        WriteCareer(CareerFile(playTime: "42.4"));

        Assert.Equal("42 min", Detail(SavegameDetail.Ids.Playtime)?.Value);
    }

    [Fact]
    public void The_games_own_save_date_is_preferred_over_the_files_timestamp()
    {
        WriteCareer(RealCareerFile);

        Assert.Equal("2024-11-30", Detail(SavegameDetail.Ids.LastPlayed)?.Value);
    }

    /// <summary>Copying a save between machines rewrites the file's timestamp; the game's own record survives that.</summary>
    [Fact]
    public void A_save_with_no_save_date_falls_back_to_the_files_timestamp()
    {
        WriteCareer(CareerFile(saveDate: null));

        Assert.NotNull(Detail(SavegameDetail.Ids.LastPlayed));
    }

    [Fact]
    public void Difficulty_is_not_shouted()
    {
        WriteCareer(RealCareerFile);

        Assert.Equal("Normal", Detail(SavegameDetail.Ids.Difficulty)?.Value);
    }

    [Fact]
    public void A_field_the_save_does_not_carry_costs_that_line_and_nothing_else()
    {
        WriteCareer(CareerFile(mapTitle: null));

        var slot = Read();

        Assert.True(slot.IsOccupied);
        Assert.Null(Detail(SavegameDetail.Ids.Map));
        Assert.NotNull(Detail(SavegameDetail.Ids.Playtime));
    }

    /// <summary>
    /// Counted across the farms' player lists, because the career file records nothing about
    /// multiplayer at all.
    /// </summary>
    [Fact]
    public void Several_players_having_joined_reads_as_multiplayer()
    {
        WriteCareer(RealCareerFile);
        WriteFarms(RealFarmsFile);

        Assert.Equal("Yes, 4 players have joined", Detail(SavegameDetail.Ids.Multiplayer)?.Value);
    }

    /// <summary>
    /// Four people on one farm and four people on four farms are different evenings, and it is also
    /// what says which farm the balance belongs to.
    /// </summary>
    [Fact]
    public void Players_spread_over_several_farms_say_so()
    {
        WriteCareer(RealCareerFile);
        WriteFarms("""
            <farms>
                <farm farmId="1" name="Mine" money="1">
                    <players>
                        <player uniqueUserId="a" lastNickname="anton" />
                    </players>
                </farm>
                <farm farmId="2" name="Theirs" money="2">
                    <players>
                        <player uniqueUserId="b" lastNickname="adamt" />
                    </players>
                </farm>
            </farms>
            """);

        Assert.Equal("Yes, 2 players have joined across 2 farms", Detail(SavegameDetail.Ids.Multiplayer)?.Value);
    }

    [Fact]
    public void One_player_reads_as_singleplayer()
    {
        WriteCareer(RealCareerFile);
        WriteFarms("""
            <farms>
                <farm farmId="1" name="Solo">
                    <players>
                        <player uniqueUserId="only-me" lastNickname="anton" />
                    </players>
                </farm>
            </farms>
            """);

        Assert.Equal("No, only ever played alone", Detail(SavegameDetail.Ids.Multiplayer)?.Value);
    }

    /// <summary>One person on two farms is still one person, and the shop farm carries no players.</summary>
    [Fact]
    public void The_same_player_on_two_farms_is_counted_once()
    {
        WriteCareer(RealCareerFile);
        WriteFarms("""
            <farms>
                <farm farmId="1" name="Mine">
                    <players>
                        <player uniqueUserId="only-me" lastNickname="anton" />
                    </players>
                </farm>
                <farm farmId="2" name="Shop" />
                <farm farmId="3" name="Also mine">
                    <players>
                        <player uniqueUserId="only-me" lastNickname="anton" />
                    </players>
                </farm>
            </farms>
            """);

        Assert.Equal("No, only ever played alone", Detail(SavegameDetail.Ids.Multiplayer)?.Value);
    }

    [Fact]
    public void A_save_with_no_farms_file_says_nothing_about_multiplayer()
    {
        WriteCareer(RealCareerFile);

        Assert.Null(Detail(SavegameDetail.Ids.Multiplayer));
    }

    /// <summary>
    /// The order is the adapter's judgment about what matters, and the row shows a prefix of it - so
    /// it is a fact worth pinning rather than an accident of how the file is laid out.
    /// </summary>
    /// <remarks>
    /// Money is in that prefix deliberately: where and how much are what tell two saves of the same
    /// map apart, and it used to sit fifth, which is off the row and into the tooltip.
    /// </remarks>
    [Fact]
    public void Where_how_much_and_when_are_said_before_anything_else()
    {
        WriteCareer(RealCareerFile);
        WriteFarms(RealFarmsFile);

        Assert.Equal(
            [SavegameDetail.Ids.Map, SavegameDetail.Ids.Money, SavegameDetail.Ids.LastPlayed],
            Read().Details.Take(3).Select(x => x.Id));
    }


    #region Money

    /// <summary>
    /// The bug this replaced: money is a property of a farm and lives in farms.xml, so a save that
    /// carried nothing under &lt;statistics&gt; reported no balance at all.
    /// </summary>
    [Fact]
    public void Money_comes_from_the_farm_rather_than_the_career_file()
    {
        WriteCareer(CareerFile());
        WriteFarms("""
            <farms>
                <farm farmId="1" name="Solo" money="1234567" />
            </farms>
            """);

        Assert.Equal(1234567d.ToString("N0"), Detail(SavegameDetail.Ids.Money)?.Value);
    }

    /// <summary>Older saves in the series kept it there, and a balance is a balance.</summary>
    [Fact]
    public void A_save_with_no_farms_file_falls_back_to_the_career_files_statistics()
    {
        WriteCareer(RealCareerFile);

        Assert.Equal(284067d.ToString("N0"), Detail(SavegameDetail.Ids.Money)?.Value);
    }

    /// <summary>The farms file is the current answer, so it wins where both are there.</summary>
    [Fact]
    public void The_farm_wins_over_the_career_files_statistics()
    {
        WriteCareer(RealCareerFile);
        WriteFarms("""
            <farms>
                <farm farmId="1" name="Solo" money="999" />
            </farms>
            """);

        Assert.Equal(999d.ToString("N0"), Detail(SavegameDetail.Ids.Money)?.Value);
    }

    /// <summary>
    /// Multiplayer has no single balance, so the first real farm's is what is shown - and it says
    /// which farm that is, because otherwise the number is unattributable.
    /// </summary>
    [Fact]
    public void With_several_farms_the_first_ones_balance_is_shown_and_named()
    {
        WriteCareer(CareerFile());
        WriteFarms("""
            <farms>
                <farm farmId="2" name="Second" money="222" />
                <farm farmId="1" name="First" money="111" />
            </farms>
            """);

        Assert.Equal($"{111d:N0} (First)", Detail(SavegameDetail.Ids.Money)?.Value);
    }

    /// <summary>Farm 0 is the shop. It exists in every save and is nobody's farm.</summary>
    [Fact]
    public void The_shop_farm_is_not_the_first_farm()
    {
        WriteCareer(CareerFile());
        WriteFarms("""
            <farms>
                <farm farmId="0" name="-" money="0" />
                <farm farmId="1" name="Mine" money="5000" />
            </farms>
            """);

        Assert.Equal(5000d.ToString("N0"), Detail(SavegameDetail.Ids.Money)?.Value);
    }

    /// <summary>A farm with no balance recorded is not a balance of nothing.</summary>
    [Fact]
    public void A_farm_with_no_money_attribute_falls_through_to_the_next_one()
    {
        WriteCareer(CareerFile());
        WriteFarms("""
            <farms>
                <farm farmId="1" name="Unfinanced" />
                <farm farmId="2" name="Second" money="42" />
            </farms>
            """);

        Assert.Equal($"{42d:N0} (Second)", Detail(SavegameDetail.Ids.Money)?.Value);
    }

    #endregion


    private SavegameDetail? Detail(string id)
        => Read().Details.FirstOrDefault(x => x.Id == id);

    private SavegameSlot Read()
        => FarmingSimulatorInstanceSavegameAdapter.ReadSlot(
            new SavegameSlotId("savegame1"), _directory.Path, NullLogger.Instance, CancellationToken.None);

    private void WriteCareer(string xml)
        => File.WriteAllText(Path.Combine(_directory.Path, "careerSavegame.xml"), xml);

    private void WriteFarms(string xml)
        => File.WriteAllText(Path.Combine(_directory.Path, "farms.xml"), xml);


    /// <summary>The shape a real FS25 save has, trimmed to the elements this adapter reads.</summary>
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

    private const string RealFarmsFile = """
        <farms>
            <farm farmId="1" name="The Dudes Inc.">
                <players>
                    <player uniqueUserId="a" lastNickname="Bujny" />
                    <player uniqueUserId="b" lastNickname="adamt" />
                    <player uniqueUserId="c" lastNickname="anton" />
                    <player uniqueUserId="d" lastNickname="FFSCow" />
                </players>
            </farm>
            <farm farmId="2" name="Shop" />
        </farms>
        """;

    private static string CareerFile(
        string? mapTitle = "Zielonka",
        string? saveDate = "2024-11-30",
        string playTime = "2717.009521")
    {
        var map = mapTitle is null ? "" : $"<mapTitle>{mapTitle}</mapTitle>";
        var saved = saveDate is null ? "" : $"<saveDate>{saveDate}</saveDate>";

        return $"""
            <careerSavegame>
                <settings>
                    <savegameName>My game save</savegameName>
                    {map}
                    {saved}
                </settings>
                <statistics>
                    <playTime>{playTime}</playTime>
                </statistics>
            </careerSavegame>
            """;
    }
}
