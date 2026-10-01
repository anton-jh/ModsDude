namespace ModsDude.Client.Wpf.Profiles;

public interface IProfileSaveService
{
    /// <summary>
    /// The save running on this profile, or null. <b>What a rebuilt editor asks before it asks the
    /// server:</b> a page that read the profile fresh while a save was uploading would draw the list
    /// the save is about to replace.
    /// </summary>
    ProfileSaveRun? Find(Guid profileId);

    /// <summary>
    /// Whether a save of this profile would be refused right now. A hint for a <c>CanExecute</c>, and
    /// not the guard - <see cref="Start"/> is what actually decides.
    /// </summary>
    bool IsSaving(Guid profileId);

    /// <summary>
    /// Whether an import into this repo would be refused right now, and by what. Read by an editor of
    /// a <em>different</em> profile, whose Save is only blocked if it has mods to import.
    /// </summary>
    string? DescribeImportBusy(Guid repoId);

    /// <summary>
    /// Imports whatever the draft pins and the repo does not hold, writes the revision, re-applies,
    /// and re-checks drift - in that order, because a mod is never registered before its file is in
    /// storage and a dependency can only name a registered version.
    /// </summary>
    /// <remarks>
    /// <b>An import that does not fully succeed stops the save.</b> The steps after it are written
    /// against the mods the repo now holds, so carrying on with a short list quietly turns "these
    /// files failed to upload" into a profile that never mentions them and an apply that treats them
    /// as unrecognised - which sends the very files the user was importing to the Recycle Bin, one
    /// confirmation click away. Nothing downstream can tell that apart from a folder full of junk,
    /// so the only place it can be caught is here, before anything is written.
    /// <para>
    /// <b>How it went is said here, as toasts</b>, whether or not an editor is on screen to draw it:
    /// the editor may have been navigated away from, and a save is a gesture rather than a page. The
    /// editor reports nothing of its own about the outcome - it only marks its rows.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The run, so the caller can mark its own rows. A refusal comes back as a run that has already
    /// finished, so there is one shape for a caller to handle rather than two.
    /// </returns>
    ProfileSaveRun Start(ProfileSaveRequest request);
}
