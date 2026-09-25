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
/// is written down is <see cref="Targets"/>, so that the folders can be read without an adapter.
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

    /// <inheritdoc cref="PersistedGame.PinnedRevision"/>
    public int? PinnedRevision => PersistedModel.PinnedRevision;

    /// <inheritdoc cref="PersistedGame.Targets"/>
    public IReadOnlyList<PersistedModTarget> Targets => PersistedModel.Targets;

    /// <summary>
    /// The same targets as the addresses the per-folder stores file things under.
    /// </summary>
    /// <remarks>
    /// Here rather than at each caller because the join is always the same one - this game's identity
    /// with each of its keys - and writing it out at every site is how one of them comes to be
    /// written with somebody else's identity.
    /// </remarks>
    public IEnumerable<ModTargetRef> TargetRefs => Targets.Select(x => new ModTargetRef(Identity, x.Key));

    internal PersistedGame PersistedModel { get; }


    public DynamicForm GetLocalSettings(IBaseGameAdapter baseAdapter)
    {
        return baseAdapter.DeserializeLocalSettings(PersistedModel.AdapterLocalSettings);
    }

    public ILocalGameAdapter GetAdapter(IBaseGameAdapter baseAdapter)
    {
        return baseAdapter.WithLocalSettings(PersistedModel.AdapterLocalSettings);
    }


    internal void Update(string name, DynamicForm localSettings, IEnumerable<PersistedModTarget> targets)
    {
        PersistedModel.Name = name;
        PersistedModel.AdapterLocalSettings = localSettings.Serialize();
        PersistedModel.Targets = [.. targets];

        PropertyChanged?.Invoke(this, new(nameof(Name)));
        PropertyChanged?.Invoke(this, new(nameof(SerializedLocalSettings)));
        PropertyChanged?.Invoke(this, new(nameof(Targets)));
    }

    internal void SetActiveProfile(ActiveProfile? activeProfile, int? pinnedRevision = null)
    {
        PersistedModel.ActiveProfile = activeProfile;
        PersistedModel.PinnedRevision = activeProfile is null ? null : pinnedRevision;

        PropertyChanged?.Invoke(this, new(nameof(ActiveProfile)));
        PropertyChanged?.Invoke(this, new(nameof(PinnedRevision)));
    }
}
