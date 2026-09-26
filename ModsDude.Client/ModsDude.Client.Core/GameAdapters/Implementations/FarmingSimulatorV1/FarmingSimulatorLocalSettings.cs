using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using System.Text.Json;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;

/// <summary>
/// Nothing to fill in. The game data folder follows from the repo's <see cref="FarmingSimulatorGameVersion"/>
/// - see <see cref="FarmingSimulatorGameDataFolder"/> - so a machine has nothing of its own to decide.
/// </summary>
/// <remarks>
/// It used to carry the folder as a picker, back when it was also how a repo said which game in the
/// series it was for. The base settings say that now, and a folder that could be pointed anywhere
/// was only a way to point an FS25 repo at an FS22 installation.
/// </remarks>
public class FarmingSimulatorLocalSettings : DynamicForm<FarmingSimulatorLocalSettings>
{
    public static FarmingSimulatorLocalSettings Deserialize(string serialized)
    {
        return JsonSerializer.Deserialize<FarmingSimulatorLocalSettings>(serialized)
            ?? throw new ArgumentException("Could not deserialize local settings");
    }
}

/// <summary>
/// Where a Farming Simulator game keeps its saves and, in a <c>mods</c> folder inside, its mods.
/// </summary>
public static class FarmingSimulatorGameDataFolder
{
    /// <summary>
    /// The folder under <c>Documents\My Games</c> for the given game, or null where the game has not
    /// made one yet. The installer has used both spellings over the years, and neither is guessable
    /// from the other, so this probes for whichever one is actually on disk.
    /// </summary>
    public static string? Find(FarmingSimulatorGameVersion gameVersion)
    {
        return Candidates(gameVersion).FirstOrDefault(Directory.Exists);
    }

    /// <inheritdoc cref="Find"/>
    /// <exception cref="UserFriendlyException">The game has not made its folder yet.</exception>
    public static string Require(FarmingSimulatorGameVersion gameVersion)
    {
        return Find(gameVersion) ?? throw new UserFriendlyException(
            "Game folder not found",
            $"ModsDude looks for this game's data in {string.Join(" or ", Candidates(gameVersion))}, and neither exists. " +
            "The game creates it the first time it is launched, so start it once and try again.");
    }


    private static IEnumerable<string> Candidates(FarmingSimulatorGameVersion gameVersion)
    {
        var myGames = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games");

        var year = (int)gameVersion;

        return [
            Path.Join(myGames, $"FarmingSimulator{year}"),
            Path.Join(myGames, $"Farming Simulator {year}")
            ];
    }
}
