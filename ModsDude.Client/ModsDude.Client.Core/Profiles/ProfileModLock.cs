namespace ModsDude.Client.Core.Profiles;

/// <summary>
/// Whether a profile's pin on a mod is allowed to move, and who said so.
/// </summary>
/// <remarks>
/// Two flags rather than one, because the two are set by different parties and have different
/// remedies. <c>ModVersion.Locked</c> is the adapter's, re-derived from the mod file at every
/// registration - a Farming Simulator map declares its maps in modDesc, so the answer comes out the
/// same for every version. <c>ModDependency.Locked</c> is the user's, and only about this profile.
/// An adapter can never set the second: locking a profile is a human decision about a human's
/// profile. See docs/02-domain-model.md#locking-in-two-places.
/// </remarks>
public readonly record struct ProfileModLock(bool ByAdapter, bool ByProfile)
{
    /// <summary>The effective answer, which is the disjunction - either flag holds the pin.</summary>
    public bool IsLocked => ByAdapter || ByProfile;

    public ProfileModLockSource Source => (ByAdapter, ByProfile) switch
    {
        (true, true) => ProfileModLockSource.Both,
        (true, false) => ProfileModLockSource.Adapter,
        (false, true) => ProfileModLockSource.Profile,
        _ => ProfileModLockSource.None
    };

    /// <summary>
    /// Whether unlocking here is enough to free the pin. False while the adapter's flag stands,
    /// which is the case worth wording carefully: there is no repo-wide user override, so clearing
    /// the per-profile flag is not the same as declaring the mod safe to bump.
    /// </summary>
    public bool CanBeUnlockedByProfile => ByProfile && ByAdapter is false;
}

/// <summary>
/// Which of the two flags a pin's lock came from. Worth distinguishing on a row: "locked because
/// this mod is version-sensitive" and "locked because I locked it here" are different situations
/// with different fixes.
/// </summary>
public enum ProfileModLockSource
{
    None,
    Adapter,
    Profile,
    Both
}
