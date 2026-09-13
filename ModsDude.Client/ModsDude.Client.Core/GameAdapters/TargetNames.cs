using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.GameAdapters;

/// <summary>
/// What to call one of a game's folders, decided in one place.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two halves of one rule.</b> A folder is called whatever the adapter named it, and its key
/// where nothing can ask an adapter what that was - and it is not named at all unless the game
/// reaches more than one, because Farming Simulator does not have a mod folder <em>called</em>
/// something.
/// </para>
/// <para>
/// <b>The name is asked for, never written down.</b> It was persisted beside the folder path for
/// one slice, so that the app-level drift notice could say <em>in the 'MP client' folder</em>
/// without hydrating an adapter - and that was a stale copy of derived data bought for a wording.
/// The path and the key are persisted because a folder read without an adapter has to be
/// <em>addressable</em>; a label is not that. See <see cref="Persistence.PersistedModTarget"/>.
/// </para>
/// <para>
/// <b>What that leaves is one surface that cannot ask</b>: the drift notice, whose whole check runs
/// off persisted state so that it works offline and for a game no loaded repo serves. Its answer is
/// not to guess a nicer word - it is that a game whose repo this account cannot see is a game
/// nothing here can act on, and the notice says <em>that</em> instead of describing folders it
/// cannot name. The key remains the fallback for the one genuinely nameless case: a savegame held
/// in a target the settings no longer produce, where there is no adapter entry left to ask about.
/// </para>
/// </remarks>
public static class TargetNames
{
    /// <param name="displayName">
    /// What the adapter called this folder, where anything could still ask it. Null falls back to
    /// the key, which is adapter-authored and legible by construction - see
    /// <c>docs/04-game-adapters.md#targets</c>.
    /// </param>
    public static string Of(TargetKey key, string? displayName)
        => displayName is { Length: > 0 } named ? named : key.Value;

    /// <summary>
    /// The same name, or null where there is only one folder and naming it would be noise.
    /// </summary>
    /// <param name="targetCount">
    /// How many folders of this kind the game reaches. One - or none, which cannot produce a name to
    /// ask about anyway - means the name is never said.
    /// </param>
    public static string? Distinguishing(TargetKey key, string? displayName, int targetCount)
        => targetCount > 1 ? Of(key, displayName) : null;

    /// <summary>
    /// What the adapter calls each folder this game reaches, keyed - for a caller that has a repo
    /// and can therefore ask.
    /// </summary>
    /// <remarks>
    /// <b>Names only, and empty is an ordinary answer.</b> A key missing from the result is a folder
    /// with no name rather than a folder that is gone: the caller is iterating the persisted target
    /// list, which stays complete, and a name that could not be read costs a word in a sentence.
    /// Settings this repo's adapter version cannot read therefore degrade to keys rather than to a
    /// shorter list - the same bargain <c>ModCatalog.ReadTargets</c> strikes, which cannot degrade
    /// the same way because a missing <em>source</em> reports what that folder holds as missing from
    /// the machine.
    /// </remarks>
    /// <param name="logger">
    /// Optional, because the only thing a failure costs here is a label, and the callers that have
    /// one are not the same as the callers that need names. Anything that hydrates this adapter for
    /// work rather than for wording logs the same failure properly.
    /// </param>
    public static IReadOnlyDictionary<TargetKey, string> Read(
        Game game, IBaseGameAdapter baseAdapter, ILogger? logger = null)
    {
        try
        {
            if (game.GetAdapter(baseAdapter).GetLocalCapabilityAdapterFactory<ILocalModAdapter>() is
                Func<ILocalModAdapter> factory)
            {
                return factory().ModTargets
                    .Where(x => x.DisplayName is { Length: > 0 })
                    .ToDictionary(x => x.Key, x => x.DisplayName!);
            }
        }
        catch (Exception exception)
        {
            logger?.LogWarning(
                exception,
                "Could not read the folder names of game {Game} from its adapter; falling back to their keys.",
                game.Identity);
        }

        return new Dictionary<TargetKey, string>();
    }
}
