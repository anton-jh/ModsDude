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
/// <b>The key is a legible fallback rather than a placeholder.</b> An adapter author picks it, it is
/// stable across settings edits, and it already ends up in a manifest's filename - so
/// <c>'server'</c> is a word somebody can tell two headings apart by. It is what the three sites that
/// cannot hydrate an adapter fall back to: the app-level notice, which is on screen before the repo
/// list loads; a hold in a folder the settings no longer name, where the target itself is gone; and
/// a persisted target list written by an adapter version that did not record names.
/// </para>
/// <para>
/// <b>A name is written down where it will be needed without an adapter.</b>
/// <see cref="Persistence.PersistedModTarget.DisplayName"/> is derived and still persisted, on the
/// same argument as the path beside it: the drift check runs off the persisted list, and a notice
/// saying <em>your MP client folder has 2 differences</em> cannot go and ask.
/// </para>
/// </remarks>
public static class TargetNames
{
    /// <param name="displayName">
    /// What the adapter called this folder, where anything could still ask it. Null falls back to
    /// the key.
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
}
