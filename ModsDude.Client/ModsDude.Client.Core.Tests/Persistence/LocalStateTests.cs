using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using System.Text.Json;

namespace ModsDude.Client.Core.Tests.Persistence;

/// <summary>
/// That the state file survives a round trip with two games in it.
/// </summary>
/// <remarks>
/// <para>
/// Worth its own test for one reason: <see cref="GameIdentity"/> is a record struct, and a record
/// struct used as a dictionary key is where System.Text.Json stops being obvious. Without
/// <see cref="GameIdentityJsonConverter.WriteAsPropertyName"/> it throws rather than writing an
/// object, so the failure is a state file that cannot be saved at all - and nothing else in the suite
/// serializes a <see cref="LocalState"/>.
/// </para>
/// <para>
/// Two games rather than one, because one of anything passes a keying test by accident.
/// </para>
/// </remarks>
public class LocalStateTests
{
    private static readonly GameIdentity _fs25 = new("farmingSimulator", "fs25");
    private static readonly GameIdentity _fs22 = new("farmingSimulator", "fs22");


    [Fact]
    public void Two_games_survive_a_round_trip_keyed_by_identity()
    {
        var state = new LocalState();
        var profile = new ActiveProfile(Guid.NewGuid(), Guid.NewGuid());

        state.Games[_fs25] = Game("Farming Simulator 25", @"C:\fs25\mods", profile);
        state.Games[_fs22] = Game("Farming Simulator 22", @"D:\fs22\mods", null);

        var read = RoundTrip(state);

        Assert.Equal(2, read.Games.Count);
        Assert.Equal("Farming Simulator 25", read.Games[_fs25].Name);
        // The key beside the folder is what names the manifest describing it, so it is the half
        // worth asserting came back readable rather than as the blank a default-serialized record
        // struct would produce.
        var target = Assert.Single(read.Games[_fs22].Targets);

        Assert.Equal(@"D:\fs22\mods", target.ModFolder);
        Assert.Equal("mods", target.Key.Value);

        // The intent is the half that cannot be re-derived from anything on disk, so it is the half
        // worth asserting survives the trip.
        Assert.Equal(profile, read.Games[_fs25].ActiveProfile);
        Assert.Null(read.Games[_fs22].ActiveProfile);
    }

    /// <summary>
    /// The identity is written as the property name it renders to, not as an object.
    /// </summary>
    /// <remarks>
    /// Asserted on the text rather than only through a round trip, because an object key would also
    /// round-trip through a converter pair that agreed with each other while making the file
    /// unreadable by eye - and this file is one somebody reads to work out what their client thinks.
    /// </remarks>
    [Fact]
    public void An_identity_is_a_property_name_rather_than_an_object()
    {
        var state = new LocalState();

        state.Games[_fs25] = Game("Farming Simulator 25", @"C:\fs25\mods", null);

        Assert.Contains("\"farmingSimulator#fs25\":", JsonSerializer.Serialize(state));
    }

    /// <summary>
    /// A game reaching no folder is an ordinary answer, and it has to stay one across a round trip:
    /// the alternative is a null that every reader has to remember to cope with.
    /// </summary>
    [Fact]
    public void A_game_reaching_no_folder_reads_back_as_reaching_none()
    {
        var state = new LocalState();

        state.Games[_fs25] = new PersistedGame
        {
            GameAdapterId = new GameAdapterId("farmingSimulator", 1),
            Name = "Farming Simulator 25",
            AdapterLocalSettings = "{}"
        };

        Assert.Empty(RoundTrip(state).Games[_fs25].Targets);
    }


    private static LocalState RoundTrip(LocalState state)
    {
        return JsonSerializer.Deserialize<LocalState>(JsonSerializer.Serialize(state))
            ?? throw new InvalidOperationException("The state did not deserialize.");
    }

    private static PersistedGame Game(string name, string modFolder, ActiveProfile? activeProfile) => new()
    {
        GameAdapterId = new GameAdapterId("farmingSimulator", 1),
        Name = name,
        AdapterLocalSettings = """{"modFolder":"C:\\mods"}""",
        Targets = [new PersistedModTarget(new TargetKey("mods"), modFolder)],
        ActiveProfile = activeProfile
    };
}
