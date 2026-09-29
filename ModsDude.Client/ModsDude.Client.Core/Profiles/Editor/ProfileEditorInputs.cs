using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Profiles.Editor;

/// <summary>Everything a <see cref="ProfileEditorState"/> is computed from.</summary>
public sealed record ProfileEditorInputs(ProfileDraft Draft, ProfileEditorCatalog Catalog)
{
    /// <summary>Whether the repo's registered versions are among what the left list offers.</summary>
    public bool IncludeRegistered { get; init; } = true;

    /// <summary>The enabled profile sources.</summary>
    public IReadOnlyList<ProfileModSource> ProfileSources { get; init; } = [];

    /// <summary>The enabled remote sources, in chip order: the first to offer a mod wins its row.</summary>
    public IReadOnlyList<RemoteSourceAnswers> RemoteSources { get; init; } = [];

    /// <summary>The version each left row's selector was set to, where it was.</summary>
    public IReadOnlyDictionary<ModKey, ModVersionKey> AvailableChoices { get; init; } = new Dictionary<ModKey, ModVersionKey>();

    /// <summary>When each saved pin entered the profile at its saved version, as the server read it.</summary>
    public IReadOnlyDictionary<ModKey, SavedPinDate> SavedDates { get; init; } = new Dictionary<ModKey, SavedPinDate>();

    public ModSearchQuery Search { get; init; } = ModSearchQuery.Empty;

    public AvailableModFilter AvailableFilter { get; init; }
    public bool ShowIgnored { get; init; }
    public ModListSort AvailableSort { get; init; } = new();

    public PinnedModFilter PinnedFilter { get; init; }
    public ModListSort PinnedSort { get; init; } = new();

    public DateTimeOffset Now { get; init; } = DateTimeOffset.Now;
}


public readonly record struct SavedPinDate(ModVersionKey Version, DateTimeOffset Added);


public sealed record RemoteSourceAnswers(ModSourceId Id, string DisplayName, IReadOnlyCollection<RemoteModOffer> Answers);


/// <summary>Another profile in this repo, read as a source. Its locks travel with its versions.</summary>
public sealed class ProfileModSource
{
    private readonly HashSet<ModVersionIdentity> _locked;


    public ProfileModSource(Guid profileId, ModSource source, IReadOnlyList<ProfileModPin> pins)
    {
        ProfileId = profileId;
        Source = source;
        Pins = pins;

        _locked = [.. pins.Where(x => x.Lock.ByProfile).Select(x => new ModVersionIdentity(x.ModId, x.VersionId))];
    }


    public Guid ProfileId { get; }
    public ModSource Source { get; }
    public IReadOnlyList<ProfileModPin> Pins { get; }

    public bool Locks(ModVersionIdentity identity) => _locked.Contains(identity);
}


public enum AvailableModFilter
{
    All,

    /// <summary>Not in the repo: a save would import it.</summary>
    New,

    /// <summary>The ordering could not place this version against the repo's newest.</summary>
    Unordered
}


public enum PinnedModFilter
{
    /// <summary>The draft, taken-out mods included.</summary>
    All,

    /// <summary>Exactly what a save would write.</summary>
    Result,

    /// <summary>What differs from the saved profile, taken-out mods included.</summary>
    Changes,

    Updates,

    Locked,

    /// <summary>Pinned, and no enabled source offers any version of it.</summary>
    NotInSources
}
