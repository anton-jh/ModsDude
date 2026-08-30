namespace ModsDude.Client.Core.Sync;

/// <summary>
/// What the last sync actually installed into one instance's mod folder.
/// </summary>
/// <remarks>
/// <para>
/// An <b>optimisation, not a source of truth</b>. Reconciliation never needs it: it works from the
/// folder's actual contents against the profile's dependencies, which is what a first sync does.
/// Losing it costs a rescan and nothing else, which is why it is never repaired, backed up or
/// treated as authoritative. <see cref="Models.ActiveProfile"/> is the opposite - a folder cannot
/// tell you which profile it was meant to be, so losing that loses the intent for good.
/// </para>
/// <para>
/// It is <b>frozen between syncs</b>, deliberately. Drift is the difference between this and the
/// folder, so a manifest that followed the folder could never detect anything - and the normal case
/// is the game updating mods while ModsDude is closed, where nothing is observing at the moment of
/// the change.
/// </para>
/// </remarks>
public sealed record SyncManifest
{
    /// <summary>
    /// Bumped when the shape changes. There is no migration and none is needed: a manifest that
    /// cannot be read is a manifest that is absent, which costs a full reconcile.
    /// </summary>
    public const int CurrentVersion = 1;


    public int Version { get; init; } = CurrentVersion;

    public required Guid InstanceId { get; init; }

    /// <summary>Which profile was applied, so a manifest can be recognised as describing another one.</summary>
    public required Guid RepoId { get; init; }
    public required Guid ProfileId { get; init; }

    public required DateTimeOffset SyncedAt { get; init; }

    /// <summary>
    /// The folder it describes. An instance repointed at a different folder has a manifest about
    /// somewhere else, which is worth noticing rather than comparing against.
    /// </summary>
    public required string ModFolder { get; init; }

    public required IReadOnlyList<SyncManifestEntry> Entries { get; init; }

    /// <summary>
    /// Files in the folder that the adapter does not read as mods - a readme, a log, a
    /// <c>modsSettings.xml</c>. Sync never touches them, and they are recorded here so the cheap
    /// drift check does not report them as additions on every launch forever.
    /// </summary>
    public IReadOnlyList<string> UnmanagedFileNames { get; init; } = [];
}

/// <param name="FileName">
/// The file's name within the mod folder, which is what a directory listing produces - the cheap
/// drift check compares names, sizes and times without opening a single archive.
/// </param>
/// <param name="ContentHash">
/// What the file contained. Not read by the cheap check at all: it is here so an uninstall knows
/// which store blob the file corresponds to, and so classification can tell two builds calling
/// themselves the same version apart.
/// </param>
public sealed record SyncManifestEntry(
    string ModId,
    string VersionId,
    string ContentHash,
    string FileName,
    long Size,
    DateTimeOffset ModifiedUtc);
