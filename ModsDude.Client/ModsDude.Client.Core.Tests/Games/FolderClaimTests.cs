using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Tests.Games;

/// <summary>
/// Which folders a game may claim.
/// </summary>
/// <remarks>
/// <para>
/// What the cross-scope duplicate-folder check collapsed to once a game owns a list of folders: a
/// game's targets are distinct from each other, and no folder belongs to two games. The third rule
/// that used to sit beside them - a name unique within the scope - is gone with the scope, since one
/// game per identity leaves nothing for a name to be unique against.
/// </para>
/// <para>
/// Over a list of games rather than through <see cref="GameRepository"/> itself: the repository reads
/// and writes the real <c>state.json</c> at a fixed path under LocalAppData, and the rule here is
/// about paths.
/// </para>
/// </remarks>
public class FolderClaimTests
{
    private static readonly GameIdentity _fs25 = new("farmingSimulator", "fs25");
    private static readonly GameIdentity _fs22 = new("farmingSimulator", "fs22");


    [Fact]
    public void A_folder_another_game_already_reaches_is_refused_by_name()
    {
        var claim = GameRepository.FindFolderConflict(
            [Game(_fs22, "Farming Simulator 22", @"C:\fs22\mods")],
            [@"C:\fs22\mods"],
            ignoredGame: _fs25);

        Assert.NotNull(claim);
        Assert.Equal(@"C:\fs22\mods", claim.Path);
        Assert.Contains("Farming Simulator 22", claim.Describe());
    }

    /// <summary>
    /// Two of one game's own targets pointing at one folder, which is what the fake adapter's three
    /// optional fields make reachable for the first time.
    /// </summary>
    /// <remarks>
    /// Refused for a different reason than a collision between games, and said differently: each
    /// target would uninstall what the other had just put there, and there is no other game to name.
    /// </remarks>
    [Fact]
    public void Two_of_one_games_targets_cannot_be_the_same_folder()
    {
        var claim = GameRepository.FindFolderConflict(
            [],
            [@"C:\beamng\server", @"C:\beamng\client", @"C:\beamng\server"],
            ignoredGame: null);

        Assert.NotNull(claim);
        Assert.Null(claim.Owner);
        Assert.Equal(@"C:\beamng\server", claim.Path);
        Assert.Contains(@"C:\beamng\server", claim.Describe());
    }

    /// <summary>
    /// A game editing its own settings is not in conflict with where it already is - otherwise saving
    /// a settings page without touching a path would refuse itself.
    /// </summary>
    [Fact]
    public void A_games_own_folders_are_not_a_conflict_with_itself()
    {
        Assert.Null(GameRepository.FindFolderConflict(
            [Game(_fs25, "Farming Simulator 25", @"C:\fs25\mods")],
            [@"C:\fs25\mods"],
            ignoredGame: _fs25));
    }

    [Fact]
    public void Distinct_folders_across_several_games_are_all_free()
    {
        Assert.Null(GameRepository.FindFolderConflict(
            [
                Game(_fs25, "Farming Simulator 25", @"C:\fs25\mods"),
                Game(_fs22, "Farming Simulator 22", @"C:\fs22\mods")
            ],
            [@"D:\beamng\server", @"D:\beamng\client"],
            ignoredGame: null));
    }

    /// <summary>
    /// Two spellings of one folder are one folder, because on Windows they are. Treating them as two
    /// would let a second game claim a folder that is already being synced to.
    /// </summary>
    [Theory]
    [InlineData(@"c:\fs22\MODS")]
    [InlineData(@"C:\fs22\mods\")]
    public void A_folder_spelled_differently_is_still_claimed(string candidate)
    {
        Assert.NotNull(GameRepository.FindFolderConflict(
            [Game(_fs22, "Farming Simulator 22", @"C:\fs22\mods")],
            [candidate],
            ignoredGame: _fs25));
    }

    /// <summary>A game whose settings point at no folder claims none, and collides with nobody.</summary>
    [Fact]
    public void Reaching_no_folder_claims_nothing()
    {
        Assert.Null(GameRepository.FindFolderConflict(
            [Game(_fs22, "Farming Simulator 22", @"C:\fs22\mods")],
            [],
            ignoredGame: null));
    }


    private static Game Game(GameIdentity identity, string name, params string[] modFolders)
    {
        return new Game(identity, new PersistedGame
        {
            GameAdapterId = new GameAdapterId("farmingSimulator", 1),
            Name = name,
            AdapterLocalSettings = "{}",
            ModFolders = [.. modFolders]
        });
    }
}
