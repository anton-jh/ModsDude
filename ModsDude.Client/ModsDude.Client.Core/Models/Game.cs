using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Persistence;
using System.ComponentModel;

namespace ModsDude.Client.Core.Models;

/// <summary>
/// One game on this machine: the policy holder. It has one active profile, holds at most one
/// savegame, and reaches however many mod folders its adapter says - its <em>targets</em>.
/// </summary>
/// <remarks>
/// <para>
/// One per <see cref="GameIdentity"/>, which is what it is keyed by rather than an id of its own.
/// Belonging to the game rather than to a repo is what lets the settings be hydrated by whichever
/// repo offers it - they are the same settings under all of them.
/// </para>
/// <para>
/// A target is a value the adapter returns and not something persisted here: there is no list for
/// the user to manage, and emptying a folder's field in the settings takes a target away again. What
/// is written down is <see cref="ModFolders"/>, so that the folders can be read without an adapter.
/// </para>
/// </remarks>
public class Game
    : INotifyPropertyChanged
{
    internal Game(GameIdentity identity, PersistedGame persistedModel)
    {
        Identity = identity;
        PersistedModel = persistedModel;
    }


    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Which game this is an installation of. The key, so it never changes.</summary>
    public GameIdentity Identity { get; }

    public GameAdapterId GameAdapterId => PersistedModel.GameAdapterId;
    public string Name => PersistedModel.Name;
    public string SerializedLocalSettings => PersistedModel.AdapterLocalSettings;
    public ActiveProfile? ActiveProfile => PersistedModel.ActiveProfile;

    /// <inheritdoc cref="PersistedGame.ModFolders"/>
    public IReadOnlyList<string> ModFolders => PersistedModel.ModFolders;

    /// <summary>
    /// The one mod folder, or null where this game reaches none.
    /// </summary>
    /// <remarks>
    /// The persisted-side counterpart of <see cref="ModTargets.SingleTargetOrNone"/>, and scaffolding
    /// for the same reason: slice 2a of Phase 10 gives a game a list of folders while every caller
    /// still reads one. Several answers null rather than throwing, because the callers are the quiet
    /// ones - the drift check and the import's source list - and a game this build cannot sync is one
    /// they have nothing to say about. Sync itself refuses it loudly, in
    /// <see cref="ModTargets.RequireSingleTarget"/>. Both die in slice 2b.
    /// </remarks>
    public string? SingleModFolderOrNone => ModFolders.Count == 1 ? ModFolders[0] : null;

    internal PersistedGame PersistedModel { get; }


    public DynamicForm GetLocalSettings(IBaseGameAdapter baseAdapter)
    {
        return baseAdapter.DeserializeLocalSettings(PersistedModel.AdapterLocalSettings);
    }

    public ILocalGameAdapter GetAdapter(IBaseGameAdapter baseAdapter)
    {
        return baseAdapter.WithLocalSettings(PersistedModel.AdapterLocalSettings);
    }


    internal void Update(string name, DynamicForm localSettings, IEnumerable<string> modFolders)
    {
        PersistedModel.Name = name;
        PersistedModel.AdapterLocalSettings = localSettings.Serialize();
        PersistedModel.ModFolders = [.. modFolders];

        PropertyChanged?.Invoke(this, new(nameof(Name)));
        PropertyChanged?.Invoke(this, new(nameof(SerializedLocalSettings)));
        PropertyChanged?.Invoke(this, new(nameof(ModFolders)));
    }

    internal void SetActiveProfile(ActiveProfile? activeProfile)
    {
        PersistedModel.ActiveProfile = activeProfile;

        PropertyChanged?.Invoke(this, new(nameof(ActiveProfile)));
    }
}
