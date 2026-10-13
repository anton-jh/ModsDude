using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Retention;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Profiles;

/// <summary>
/// One snapshot of a profile's mod list, and the unit a profile is edited in.
/// </summary>
/// <remarks>
/// <para>
/// <b>A revision is immutable once constructed.</b> There is no method here that changes what it
/// pins, and no API route names a revision to write to - a write addresses the profile and means its
/// head. That is what makes an old revision read-only: not a flag somebody has to remember to check,
/// but the absence of anything that could address it. Rolling back does not reopen one either; it
/// copies an old snapshot forward as a new revision, so nothing that was ever true stops being
/// recorded.
/// </para>
/// <para>
/// A snapshot rather than a changeset. The mod list <em>is</em> the profile, so replaying a log to
/// answer "what did revision 6 pin?" would make every read of history a fold, and the
/// one-version-per-mod rule would stop being an index and become a hope. The cost is rows - a
/// two-thousand-mod profile writes two thousand narrow rows per revision - which is the cheap half
/// of the trade at the volumes this targets.
/// </para>
/// </remarks>
public class ProfileRevision
{
    /// <summary>
    /// A label is read by people scanning a list, not matched on, so this only has to stop somebody
    /// pasting an essay into the history.
    /// </summary>
    public const int MaximumLabelLength = 100;


    private readonly List<ProfileModPin> _pins = [];


    // ef
    private ProfileRevision()
    {
        Changes = ProfileRevisionChanges.None;
    }

    /// <summary>
    /// Reached through <see cref="Profile.CreateRevision"/>, which is what decides the number and
    /// moves the head. Constructing one directly would let a caller invent a number.
    /// </summary>
    internal ProfileRevision(
        RepoId repoId,
        ProfileId profileId,
        RevisionNumber number,
        IReadOnlyCollection<ProfileModPin> pins,
        IReadOnlyCollection<ProfileModPin> previousPins,
        UserId createdBy,
        DateTime created,
        string? label,
        ProfileRevisionOrigin origin,
        ProfileId? sourceProfileId,
        RevisionNumber? sourceRevision)
    {
        if (label is { Length: > MaximumLabelLength })
        {
            throw new DomainValidationException($"A revision label cannot be longer than {MaximumLabelLength} characters.");
        }

        // Backed by a unique index: one mod at two versions is a list the sync engine cannot resolve.
        var duplicate = pins.GroupBy(x => x.ModId).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Cannot pin mod '{duplicate.Key.Value}' more than once in the same revision");
        }

        _pins.AddRange(StampAdded(pins, previousPins, created));

        RepoId = repoId;
        ProfileId = profileId;
        Number = number;
        Changes = ProfileRevisionChanges.Between(previousPins, _pins);
        ModCount = _pins.Count;
        CreatedBy = createdBy;
        Created = created;
        Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        Origin = origin;
        SourceProfileId = sourceProfileId;
        SourceRevision = sourceRevision;
    }


    public RepoId RepoId { get; private set; }
    public ProfileId ProfileId { get; private set; }
    public RevisionNumber Number { get; private set; }

    public IReadOnlyList<ProfileModPin> Pins => _pins;

    /// <summary>
    /// How many mods this revision pins, denormalized so the history list does not count two
    /// thousand rows per row it renders.
    /// </summary>
    public int ModCount { get; private set; }

    /// <summary>
    /// What this revision did to the one before it. Recorded at creation rather than derived on
    /// demand: it is a fact about an immutable pair, and the history list would otherwise diff every
    /// adjacent pair of two-thousand-mod snapshots to render a summary line.
    /// </summary>
    public ProfileRevisionChanges Changes { get; private set; }

    public UserId CreatedBy { get; private set; }
    public DateTime Created { get; private set; }

    /// <summary>What somebody called this save, or <c>null</c> where nobody named it.</summary>
    public string? Label { get; private set; }

    public ProfileRevisionOrigin Origin { get; private set; }

    /// <summary>
    /// The profile this revision's contents were copied out of, set only for
    /// <see cref="ProfileRevisionOrigin.Copied"/> - a profile branched off another one.
    /// </summary>
    public ProfileId? SourceProfileId { get; private set; }

    /// <summary>
    /// The revision this one's contents were copied from, for
    /// <see cref="ProfileRevisionOrigin.Restored"/> and <see cref="ProfileRevisionOrigin.Copied"/>.
    /// It is what lets the history say "restored from 3" rather than presenting a rollback as an
    /// ordinary edit that happens to look like an old one.
    /// </summary>
    public RevisionNumber? SourceRevision { get; private set; }

    /// <summary>
    /// When the retention job will delete this revision, or <c>null</c> where it is not scheduled.
    /// Written only by retention - see <see cref="RetentionPolicy"/> - and set together with
    /// <see cref="DeletionReason"/>.
    /// </summary>
    public DateOnly? DeletionScheduledFor { get; private set; }

    /// <summary>Why <see cref="DeletionScheduledFor"/> was set. The schedule stands only while this still holds.</summary>
    public DeletionReason? DeletionReason { get; private set; }


    /// <summary>
    /// Copies each pin with its <see cref="ProfileModPin.Added"/>: the one it had in the previous
    /// revision if that pinned the same version of the same mod, and this revision's own instant for
    /// anything else. Compared against the revision this one follows, whichever way it was made, so a
    /// restore stamps only what it actually changed.
    /// </summary>
    private static IEnumerable<ProfileModPin> StampAdded(
        IReadOnlyCollection<ProfileModPin> pins,
        IReadOnlyCollection<ProfileModPin> previousPins,
        DateTime created)
    {
        var previous = previousPins.ToDictionary(x => x.ModId);

        return pins.Select(pin => pin.CopyAddedAt(
            previous.TryGetValue(pin.ModId, out var before) && before.VersionId == pin.VersionId
                ? before.Added
                : created));
    }
}


/// <summary>Why a revision exists, which is what the history list reads to describe it.</summary>
public enum ProfileRevisionOrigin
{
    /// <summary>The profile's first revision, made with the profile itself.</summary>
    Created,

    /// <summary>Somebody saved the mod list.</summary>
    Saved,

    /// <summary>An older revision of this profile, copied forward to the front.</summary>
    Restored,

    /// <summary>A revision of another profile, copied into this one when it was branched off.</summary>
    Copied
}


/// <summary>
/// Where a revision sits in its profile's history. Contiguous and one-based, so "revision 7" is a
/// thing somebody can say out loud and find.
/// </summary>
public readonly record struct RevisionNumber(int Value) : IComparable<RevisionNumber>
{
    /// <summary>
    /// What a profile's head is between its construction and its first revision - a state that only
    /// exists inside the transaction that creates the profile, and that never reaches the database.
    /// </summary>
    public static RevisionNumber None { get; } = new(0);

    public RevisionNumber Next() => new(Value + 1);

    public int CompareTo(RevisionNumber other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();
}
