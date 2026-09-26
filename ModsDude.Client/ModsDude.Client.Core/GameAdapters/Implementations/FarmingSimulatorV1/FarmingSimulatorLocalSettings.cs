using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

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
/// Where a Farming Simulator game keeps its saves and settings, and where it loads mods from.
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


    /// <summary>
    /// Where the game loads mods from: the <c>mods</c> folder inside its data folder, unless the
    /// player has pointed it elsewhere with <c>modsDirectoryOverride</c> in <c>gameSettings.xml</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read every time rather than remembered: an override switched on or off between two launches
    /// moves the folder the game reads, and a sync into the one it stopped reading would install
    /// mods nobody sees.
    /// </para>
    /// <para>
    /// <b>No file is an answer; an unreadable one is not.</b> A game that has never saved its settings
    /// has no override, so a missing file means the default folder. A file that is there but will not
    /// parse - half-written by the game, say - is refused rather than read as "no override", because
    /// guessing wrong repoints the game at a folder full of different mods.
    /// </para>
    /// </remarks>
    /// <exception cref="UserFriendlyException"><c>gameSettings.xml</c> is there and cannot be read.</exception>
    public static string FindModsFolder(string gameDataFolder)
    {
        var defaultFolder = Path.Join(gameDataFolder, "mods");
        var settingsFile = Path.Join(gameDataFolder, _gameSettingsFile);

        if (File.Exists(settingsFile) is false)
        {
            return defaultFolder;
        }

        try
        {
            // Shared with the game, which may well have it open.
            using var stream = new FileStream(settingsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var element = XDocument.Load(stream).Root?.Element("modsDirectoryOverride");

            if (element is null || (bool?)element.Attribute("active") is not true)
            {
                return defaultFolder;
            }

            var directory = (string?)element.Attribute("directory");

            // Switched on with nowhere to go, which the game can only treat as off.
            return string.IsNullOrWhiteSpace(directory)
                ? defaultFolder
                : Path.GetFullPath(directory, gameDataFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or FormatException or ArgumentException or NotSupportedException)
        {
            throw new UserFriendlyException(
                "Could not read the game's settings",
                $"ModsDude reads {settingsFile} to find out whether the game loads its mods from somewhere other than {defaultFolder}, " +
                "and it could not be read. If the game is running, try again once it has finished saving.",
                exception);
        }
    }


    private const string _gameSettingsFile = "gameSettings.xml";

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
