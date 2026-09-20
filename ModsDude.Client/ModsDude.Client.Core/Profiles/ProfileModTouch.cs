namespace ModsDude.Client.Core.Profiles;

/// <summary>
/// What a draft has done to one mod, measured against what the server holds. <see cref="None"/> for a
/// mod the draft has left exactly as it found it - including one that was changed and changed back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Derived, never recorded.</b> Keeping a log of what was clicked would report a mod pinned at
/// another version and then pinned back as touched twice over; comparing with the server's list
/// reports it as not touched at all, which is what saving it would do.
/// </para>
/// <para>
/// The same rule as <see cref="ProfileModListDiff"/> and <see cref="ProfileRevisionComparison"/>: a
/// mod's version, and the profile's own lock. The adapter's lock belongs to the version, so it is
/// nothing a draft can have done.
/// </para>
/// </remarks>
public enum ProfileModTouch
{
    None,

    /// <summary>The server's list does not hold it, and the draft's does.</summary>
    Added,

    VersionChanged,

    LockChanged,

    VersionAndLockChanged,

    /// <summary>The server's list holds it, and the draft's does not.</summary>
    TakenOut
}


public static class ProfileModTouches
{
    /// <param name="original">What the server pins of this mod, or null where it pins none.</param>
    /// <param name="desired">What the draft pins of it, or null where it pins none.</param>
    public static ProfileModTouch Classify(ProfileModPin? original, ProfileModPin? desired)
    {
        if (original is null)
        {
            return desired is null ? ProfileModTouch.None : ProfileModTouch.Added;
        }

        if (desired is null)
        {
            return ProfileModTouch.TakenOut;
        }

        return (original.VersionId != desired.VersionId, original.Lock.ByProfile != desired.Lock.ByProfile) switch
        {
            (true, true) => ProfileModTouch.VersionAndLockChanged,
            (true, false) => ProfileModTouch.VersionChanged,
            (false, true) => ProfileModTouch.LockChanged,
            _ => ProfileModTouch.None
        };
    }

    /// <summary>
    /// What a touched mod's mark says when it is hovered: what the saved profile holds, and what the
    /// draft would make of it. Null for a mod nothing has been done to.
    /// </summary>
    public static string? Describe(ProfileModPin? original, ProfileModPin? desired)
    {
        var lockText = desired?.Lock.ByProfile is true
            ? "Locked in this profile."
            : "Unlocked in this profile.";

        return Classify(original, desired) switch
        {
            ProfileModTouch.Added => $"Not in the saved profile. Saving pins {desired!.VersionId}.",
            ProfileModTouch.TakenOut => $"In the saved profile at {original!.VersionId}. Saving takes it out.",
            ProfileModTouch.VersionChanged => $"Was {original!.VersionId} in the saved profile, now {desired!.VersionId}.",
            ProfileModTouch.LockChanged => $"{lockText} Not saved yet.",
            ProfileModTouch.VersionAndLockChanged
                => $"Was {original!.VersionId} in the saved profile, now {desired!.VersionId}. {lockText}",
            _ => null
        };
    }

    /// <summary>The same words for a change a comparison has already found.</summary>
    public static string? Describe(ProfileModChange change) => Describe(
        change.FromVersionId is { } from
            ? new ProfileModPin(change.ModId, from, new ProfileModLock(false, change.FromLocked))
            : null,
        change.ToVersionId is { } to
            ? new ProfileModPin(change.ModId, to, new ProfileModLock(false, change.ToLocked))
            : null);

    /// <summary>The same answer for a change a comparison has already found.</summary>
    public static ProfileModTouch Of(ProfileModChange change) => change.Kind switch
    {
        ProfileModChangeKind.Added => ProfileModTouch.Added,
        ProfileModChangeKind.Removed => ProfileModTouch.TakenOut,
        _ => (change.VersionMoved, change.LockChanged) switch
        {
            (true, true) => ProfileModTouch.VersionAndLockChanged,
            (true, false) => ProfileModTouch.VersionChanged,
            (false, true) => ProfileModTouch.LockChanged,
            _ => ProfileModTouch.None
        }
    };
}
