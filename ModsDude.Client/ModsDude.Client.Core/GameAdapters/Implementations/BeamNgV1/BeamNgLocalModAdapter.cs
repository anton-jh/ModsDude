using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;

/// <summary>
/// The game's mods folder, a BeamMP server's and the BeamMP launcher's cache - whichever of them
/// this machine's settings name.
/// </summary>
/// <remarks>
/// <para>
/// <b>One file name, three spellings of it.</b> The game and the server hold a mod under the name
/// the repo registered for it. The launcher's cache holds it under that name with its hash
/// appended, which is the only name the launcher looks for before downloading it from the server -
/// so a profile applied to both the server and the cache is one the server's players never wait for.
/// </para>
/// <para>
/// <b>No hardlinks anywhere yet.</b> Each target switches them on once somebody has checked that
/// nothing writing to it rewrites a mod file in place: the game's in-game repository updater, the
/// server, and the launcher's cache handling.
/// </para>
/// </remarks>
public class BeamNgLocalModAdapter(BeamNgLocalSettings settings, ILoggerFactory? loggerFactory = null)
    : BeamNgBaseModAdapter(loggerFactory), ILocalModAdapter
{
    /// <summary>
    /// Every folder the settings name. The launcher's cache is <see cref="ModTarget.Shared"/>: it
    /// holds what every server the player has joined sent them, and those are not the profile's.
    /// </summary>
    public ModTargets ModTargets => new(Targets());


    public Task<IEnumerable<LocalMod>> GetInstalledMods(ModTarget target, Func<string, bool> skip, CancellationToken cancellationToken)
    {
        return ReadFolder(target.Path, skip, IsCache(target) ? BeamMpCacheName.ServerFileName : x => x, cancellationToken);
    }

    /// <summary>
    /// One archive per mod, under the name the repo registered for it - or in the launcher's cache,
    /// under that name with the hash the launcher looks for. Only the game's own folder has anything
    /// else to change: its list of which mods are switched on.
    /// </summary>
    /// <remarks>
    /// Where the repo has nothing usable registered, the file keeps the name it has, or gets the id.
    /// </remarks>
    public ModLayout Layout(ModLayoutContext context)
    {
        var cache = IsCache(context.Target);

        var placements = context.Desired
            .Select(x => new ModPlacement(x.ModId, cache ? BeamMpCacheName.For(NameOf(x, cache), x.ContentHash) : NameOf(x, cache)))
            .ToList();

        IReadOnlyList<GameFileEdit> managedFiles = context.Target.Key == BeamNgTarget.Game
            ? [BeamNgModDatabase.Activate(placements.Select(x => x.FileName))]
            : [];

        return new ModLayout(placements, managedFiles);
    }


    private IEnumerable<ModTarget> Targets()
    {
        if (NonBlank(settings.GameModsFolder) is string game)
        {
            yield return new ModTarget(BeamNgTarget.Game, "Game", game);
        }

        if (NonBlank(settings.ServerModsFolder) is string server)
        {
            yield return new ModTarget(BeamNgTarget.Server, "BeamMP server", server);
        }

        if (NonBlank(settings.LauncherCacheFolder) is string cache)
        {
            yield return new ModTarget(BeamNgTarget.Client, "BeamMP client", cache) { Shared = true };
        }
    }

    private static string NameOf(ModLayoutMod mod, bool cache)
    {
        var installed = mod.InstalledFileName is string name && cache
            ? BeamMpCacheName.ServerFileName(name)
            : mod.InstalledFileName;

        return mod.FileName?.Value ?? installed ?? $"{mod.ModId.Value}.zip";
    }

    private static bool IsCache(ModTarget target) => target.Key == BeamNgTarget.Client;

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
