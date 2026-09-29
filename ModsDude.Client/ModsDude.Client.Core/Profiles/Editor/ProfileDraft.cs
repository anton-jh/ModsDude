using ModsDude.Client.Core.Models;
using System.Collections.Immutable;

namespace ModsDude.Client.Core.Profiles.Editor;

/// <summary>
/// A profile's mod list and ignore list as saved, and as the editor would have them. Immutable: every
/// edit returns a new draft, which is what makes undo a matter of keeping the old one.
/// </summary>
/// <remarks>
/// Pins carry only the profile's own lock. The adapter's belongs to the version and is read from it.
/// </remarks>
public sealed record ProfileDraft
{
    public ProfileDraft(IEnumerable<ProfileModPin> saved, IEnumerable<ModKey> savedIgnored)
    {
        Saved = saved.Select(Normalize).ToImmutableDictionary(x => x.ModId);
        SavedIgnored = [.. savedIgnored];
        Pins = Saved;
        Ignored = SavedIgnored;
    }


    public ImmutableDictionary<ModKey, ProfileModPin> Saved { get; }
    public ImmutableHashSet<ModKey> SavedIgnored { get; }

    public ImmutableDictionary<ModKey, ProfileModPin> Pins { get; private init; }

    /// <summary>What the user has ignored, pinned or not. A pin wins; see <see cref="IgnoredToWrite"/>.</summary>
    public ImmutableHashSet<ModKey> Ignored { get; private init; }


    /// <summary>The ignore list as a save writes it: the server refuses one that overlaps the pins.</summary>
    public ImmutableHashSet<ModKey> IgnoredToWrite => Ignored.Except(Pins.Keys);

    public ProfileModListChanges Changes => ProfileModListDiff.Compute(Saved.Values, Pins.Values);

    public bool HasPinChanges => Changes.IsEmpty is false;

    public bool HasIgnoreChanges => IgnoredToWrite.SetEquals(SavedIgnored) is false;

    public bool HasChanges => HasPinChanges || HasIgnoreChanges;

    /// <summary>Every mod on either side, which is every mod the right list has a row for.</summary>
    public IEnumerable<ModKey> AllMods => Pins.Keys.Union(Saved.Keys);


    /// <summary>Whether the two would save the same thing from the same starting point.</summary>
    public bool IsSameAs(ProfileDraft other)
        => ReferenceEquals(this, other)
        || (ReferenceEquals(Saved, other.Saved)
            && ReferenceEquals(SavedIgnored, other.SavedIgnored)
            && Pins.Count == other.Pins.Count
            && Pins.All(x => other.Pins.TryGetValue(x.Key, out var pin) && pin == x.Value)
            && Ignored.SetEquals(other.Ignored));

    public ProfileModTouch TouchOf(ModKey modId)
        => ProfileModTouches.Classify(Saved.GetValueOrDefault(modId), Pins.GetValueOrDefault(modId));

    public ProfileDraft Pin(ProfileModPin pin) => Pin([pin]);

    public ProfileDraft Pin(IEnumerable<ProfileModPin> pins)
        => this with { Pins = Pins.SetItems(pins.Select(x => KeyValuePair.Create(x.ModId, Normalize(x)))) };

    public ProfileDraft Remove(IEnumerable<ModKey> mods) => this with { Pins = Pins.RemoveRange(mods) };

    public ProfileDraft SetVersion(ModKey modId, ModVersionKey version)
        => Pins.TryGetValue(modId, out var pin) ? Pin(pin with { VersionId = version }) : this;

    public ProfileDraft SetLocked(IEnumerable<ModKey> mods, bool locked)
        => Pin(mods
            .Where(Pins.ContainsKey)
            .Select(x => Pins[x] with { Lock = new ProfileModLock(false, locked) }));

    public ProfileDraft SetIgnored(IEnumerable<ModKey> mods, bool ignored)
        => this with { Ignored = ignored ? Ignored.Union(mods) : Ignored.Except(mods) };

    /// <summary>Puts each mod back the way the saved profile has it: pinned as it was, or not at all.</summary>
    public ProfileDraft Revert(IEnumerable<ModKey> mods)
    {
        var pins = Pins.ToBuilder();

        foreach (var modId in mods)
        {
            if (Saved.TryGetValue(modId, out var saved))
            {
                pins[modId] = saved;
            }
            else
            {
                pins.Remove(modId);
            }
        }

        return this with { Pins = pins.ToImmutable() };
    }

    public ProfileDraft RevertAll() => this with { Pins = Saved, Ignored = SavedIgnored };

    public ProfileDraft ReplacePins(IEnumerable<ProfileModPin> pins)
        => this with { Pins = pins.Select(Normalize).ToImmutableDictionary(x => x.ModId) };


    private static ProfileModPin Normalize(ProfileModPin pin)
        => pin.Lock.ByAdapter ? pin with { Lock = new ProfileModLock(false, pin.Lock.ByProfile) } : pin;
}


/// <summary>What one undo step of the editor restores: the draft, and which sources are loaded.</summary>
public sealed record ProfileEditorSnapshot(ProfileDraft Draft, ImmutableHashSet<ModSourceId> Loaded);
