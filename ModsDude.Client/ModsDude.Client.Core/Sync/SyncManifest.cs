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
    /// <remarks>
    /// Bumped to 2 for <see cref="SyncManifestEntry.Locked"/>. A version 1 manifest would deserialize
    /// with every entry unlocked, which reads as "no locked mod drifted" - the one thing the drift
    /// notice exists to say loudly. Discarding it costs a reconcile; believing it costs a savegame.
    /// </remarks>
    public const int CurrentVersion = 2;


    public int Version { get; init; } = CurrentVersion;

    public required Guid InstanceId { get; init; }

    /// <summary>Which profile was applied, so a manifest can be recognised as describing another one.</summary>
    public required Guid RepoId { get; init; }
    public required Guid ProfileId { get; init; }

    /// <summary>
    /// What the profile was called when it was applied. Recorded rather than looked up so the drift
    /// notice can name it at startup, offline, and for a repo whose profile list has not been read
    /// yet - which is every repo but the one the user happens to be standing in.
    /// </summary>
    public string? ProfileName { get; init; }

    /// <summary>
    /// Which revision of the profile was applied, so a folder can say which version of the list it
    /// was made to match - "this folder is on revision 6, the profile is at 8" rather than a bare
    /// "something differs".
    /// </summary>
    /// <remarks>
    /// Nullable, and <see cref="CurrentVersion"/> is deliberately <b>not</b> bumped for it. A
    /// manifest written before revisions existed deserializes with this null, which reads as "which
    /// revision this was is not recorded" - true, and harmless. The bump for
    /// <see cref="SyncManifestEntry.Locked"/> was needed because the old data answered its question
    /// <em>wrongly</em>; this one answers it not at all, and discarding a manifest costs a full
    /// rescan for nothing.
    /// </remarks>
    public int? ProfileRevision { get; init; }

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
    DateTimeOffset ModifiedUtc)
{
    /// <summary>
    /// Whether the profile locked this mod when it was applied. Recorded so the cheap check can name
    /// the dangerous case - a locked map the game updated underneath the profile - without the
    /// profile's current dependencies in hand, which at startup nothing has.
    /// </summary>
    public bool Locked { get; init; }

    /// <summary>For saying which mod, in a notice that has no mod list to look the name up in.</summary>
    public string? DisplayName { get; init; }
}
