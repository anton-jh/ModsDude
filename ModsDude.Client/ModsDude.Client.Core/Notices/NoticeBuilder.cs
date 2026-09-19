using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Notices;

/// <summary>
/// Turns what the drift check found into the list of notices the column draws.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure, and separate from the shell for the reason <see cref="SavegameDriftRules"/> is separate
/// from the I/O.</b> Listing folders, hashing slots and asking what a repo's membership is happen
/// around this and never inside it, so the rule that decides what the user is told - and in which
/// order, and with which button on it - is one function that can be exercised exhaustively. The
/// sentences were the half of the old drift card that no test ever reached.
/// </para>
/// <para>
/// <b>What decomposes, and what does not.</b> A savegame is its own notice because each one is a
/// different slot with a different remedy. Locked mods are <em>one</em> notice per folder because
/// they are one event - an update-all - with one remedy, and twenty cards for one gesture is the
/// wall the single card was right to be afraid of. A corrupt store blob belongs to the volume, not
/// to the game that happened to catch it.
/// </para>
/// <para>
/// <b>Order is by what is at stake, by game.</b> Games sort by their worst notice and stay together,
/// so a machine running a dedicated server and an MP client reads as one game in trouble rather than
/// as three. Inside a game it is severity again.
/// </para>
/// </remarks>
public static class NoticeBuilder
{
    /// <summary>Separates the parts of a signature. Anything that cannot occur inside one would do.</summary>
    private const string SignatureSeparator = "|~|";


    public static IReadOnlyList<Notice> Build(
        IReadOnlyList<TargetDrift> drifted,
        IReadOnlyList<CorruptedBlob> corruption,
        INoticeEnvironment environment)
    {
        var notices = new List<Notice>();

        foreach (var game in drifted.Where(x => x.IsDrifted).GroupBy(x => x.Game.Identity))
        {
            notices.AddRange(ForGame([.. game], environment));
        }

        notices.AddRange(ForStore(corruption));

        return Sort(notices);
    }


    /// <summary>
    /// Everything worth saying about one game, across every folder it reaches.
    /// </summary>
    /// <remarks>
    /// Per game rather than per entry because the savegame half is placed per folder and read per
    /// game: a held save sits in one target's savegame folder, so the entry carrying it is not
    /// necessarily the entry whose mods drifted, and a game that said nothing about its savegames
    /// because the drifted one was in another folder is exactly the silence this avoids.
    /// </remarks>
    private static IEnumerable<Notice> ForGame(IReadOnlyList<TargetDrift> entries, INoticeEnvironment environment)
    {
        var game = entries[0].Game;
        var repo = environment.FindRepo(game.Identity, game.ActiveProfile?.RepoId);

        // Everything this column can offer goes through the repo: re-applying needs the mod list,
        // reviewing needs a page in a sidebar, naming a folder needs the adapter. A game whose repo
        // this account cannot see is a game nothing here can act on, and saying that once is better
        // than counting changed files under buttons that do nothing.
        if (repo is null)
        {
            return [Unreachable(game.Name, game.Identity, environment.ReposLoaded)];
        }

        var folders = environment.FolderNames(game.Identity);
        var notices = new List<Notice>();

        foreach (var entry in entries)
        {
            if (Folder(entry, repo, folders, environment) is Notice folder)
            {
                notices.Add(folder);
            }

            if (Locked(entry, repo, folders) is Notice locked)
            {
                notices.Add(locked);
            }
        }

        // Deduplicated by savegame rather than by slot: the kinds are not exclusive, so one save can
        // arrive carrying several, and they are said together on one card.
        notices.AddRange(entries
            .SelectMany(x => x.Report.SavegameDrift)
            .GroupBy(x => x.SavegameId)
            .Select(x => Savegame([.. x], game, repo, environment)));

        // Only where there is more than one card to put it above - a single notice names its own
        // game in its headline, and a heading repeating it would be furniture.
        return notices.Count > 1
            ? notices.Select(x => x with { GroupLabel = game.Name })
            : notices;
    }


    /// <summary>
    /// The mod half, for one folder. Null where this entry is here for its savegames alone.
    /// </summary>
    private static Notice? Folder(
        TargetDrift entry,
        NoticeRepo repo,
        IReadOnlyDictionary<TargetKey, string> folders,
        INoticeEnvironment environment)
    {
        var report = entry.Report;
        var game = entry.Game;

        // A status the column does not fire for on its own means the savegame half is the whole
        // reason this game is on screen, and that has its own card.
        if (report.Status is not (DriftStatus.Drifted or DriftStatus.NotApplied
            or DriftStatus.FolderRepointed or DriftStatus.NeverSynced))
        {
            return null;
        }

        var named = FolderName(entry, folders);
        var profile = entry.ProfileName is string name ? $"'{name}'" : "the applied profile";
        var files = report.Added.Count + report.Removed.Count + report.Changed.Count;
        var where = named is string folder ? $"the '{folder}' folder" : "the mod folder";
        var inFolder = named is string it ? $" in the '{it}' folder" : "";

        // Named for the consequence rather than for the condition, which is why an activation that
        // did not land says where the folder still is rather than what failed. The folder is in the
        // headline now that a headline is about one of them - the single card's was about all of a
        // game's folders at once, where naming one would have been a lie.
        var (headline, body, severity) = report.Status switch
        {
            DriftStatus.Drifted => (
                $"'{game.Name}' no longer matches {profile}{inFolder}",
                DescribeDrifted(report, files, where, named),
                NoticeSeverity.Warning),

            DriftStatus.NotApplied => (
                report.AppliedProfileName is string applied
                    ? $"'{game.Name}' is still on '{applied}'{inFolder}"
                    : $"'{game.Name}' is still on the mod list it was last applied to{inFolder}",
                $"Disregard if the profile is currently applying",
                NoticeSeverity.Pending),

            DriftStatus.FolderRepointed => (
                $"'{game.Name}' has a mod folder nothing has been applied to",
                $"The settings now point{inFolder} at {entry.Target?.ModFolder ?? "another folder"}, "
                    + "which nothing has been applied to. Re-applying is what fills it in.",
                NoticeSeverity.Pending),

            // Deliberately not naming the profile: the name a notice would have is the one the
            // manifest recorded, and the whole of this status is that there is no manifest.
            _ => (
                $"Nothing has been applied to '{game.Name}'{inFolder}",
                $"This game follows a profile and there is no record of it ever being applied{inFolder} - "
                    + "either it never was, or the record was lost. Applying it is what makes the two agree, "
                    + "and until then nothing here can tell you whether the mods are right.",
                NoticeSeverity.Pending)
        };

        var canReview = CanReview(entry, repo);

        // Review leads wherever it is offered: what the folder holds now is usually what the user
        // meant to end up with, and re-applying before looking throws it away. It is not offered for
        // the three pending statuses - there is nothing in the folder to look at yet - so re-apply
        // leads there by being the only thing to do.
        var reviewLeads = canReview && report.Status is DriftStatus.Drifted;

        return Make(
            $"drift/{game.Identity}/{entry.Target?.Target.Key.Value ?? "-"}",
            severity,
            headline,
            body,
            // The drifted files are by definition versions the user now has and the repo may not, so
            // the warning doubles as the first step of the flow they came back to perform anyway.
            footnote: files > 0 && canReview
                ? "The versions now on disk may not be in the repo. Opening the mod list is where they get imported."
                : null,
            actions: ModActions(entry, repo, reviewLeads, canReview, environment),
            subject: Subject(entry, repo));
    }

    private static string DescribeDrifted(DriftReport report, int files, string where, string? named)
    {
        var parts = new List<string>();

        if (report.Changed.Count > 0) parts.Add($"{report.Changed.Count} replaced");
        if (report.Added.Count > 0) parts.Add($"{report.Added.Count} added");
        if (report.Removed.Count > 0) parts.Add($"{report.Removed.Count} removed");

        var folder = files > 0
            ? $"{string.Join(", ", parts)} in {where} since it was last applied. "
                + "Updating mods from inside the game looks like this."
            : "";

        // The half of drift no directory listing can find: the folder is exactly what was installed,
        // and what was installed is no longer what the profile says.
        var revision = report.ProfileHasMoved
            ? $"{(named is string it ? $"The '{it}' folder" : "This folder")} was made to match revision "
                + $"{report.AppliedRevision}; the profile is now at revision {report.CurrentRevision}."
            : "";

        var pins = report.ProfileChangedMods.Count > 0
            ? $"{report.ProfileChangedMods.Count} mods in {where} are pinned differently than what is "
                + "installed - somebody has edited the profile since."
            : "";

        return string.Join(' ', new[] { folder, revision, pins }.Where(x => x.Length > 0));
    }


    /// <summary>
    /// The dangerous half of a drifted folder, on its own card and in its own colour.
    /// </summary>
    /// <remarks>
    /// <b>One card per folder, not per mod.</b> An unlocked mod at the wrong version is untidy and a
    /// locked one is a damaged savegame waiting to happen - but twenty locked mods are one update-all
    /// with one remedy, and twenty cards saying so would be the wall a column has to avoid being.
    /// The names go in the body, which is where a list belongs.
    /// </remarks>
    private static Notice? Locked(
        TargetDrift entry, NoticeRepo repo, IReadOnlyDictionary<TargetKey, string> folders)
    {
        var locked = entry.Report.LockedDrift;

        if (locked.Count == 0)
        {
            return null;
        }

        var game = entry.Game;
        var inFolder = FolderName(entry, folders) is string it ? $" in the '{it}' folder" : "";

        var first = locked[0];
        var version = first.AppliedVersion is string applied ? $" at {applied}" : "";

        var headline = locked.Count == 1
            ? first.Reason switch
            {
                LockedDriftReason.FileRemoved => $"'{first.DisplayName}' is locked{version} and is no longer installed",
                LockedDriftReason.ProfileMoved => $"'{first.DisplayName}' is locked and the profile has moved off it",
                _ => $"'{first.DisplayName}' is locked{version} and its file has changed"
            }
            : $"{locked.Count} locked mods in '{game.Name}' are not what they were locked at";

        var what = first.Reason switch
        {
            LockedDriftReason.FileRemoved =>
                $"'{first.DisplayName}' is locked{version} and is no longer in "
                    + $"{(inFolder.Length > 0 ? inFolder[4..] : "the mod folder")}.",

            LockedDriftReason.ProfileMoved =>
                $"'{first.DisplayName}' is locked and the profile no longer pins the version installed here{version}.",

            _ =>
                $"'{first.DisplayName}' is locked{version} and its file has changed since it was applied."
        };

        // Up to three more by name before falling back to a count. A name is something the user can
        // act on and a number is not, and three is where a card stops being readable at a glance.
        var rest = locked.Skip(1).ToList();

        var more = rest.Count switch
        {
            0 => "",
            <= 3 => $" So {(rest.Count == 1 ? "is" : "are")} {Names(rest)}.",
            _ => $" So are {Names(rest.Take(3))}, and {rest.Count - 3} more."
        };

        var canReview = CanReview(entry, repo);

        return Make(
            $"locked/{game.Identity}/{entry.Target?.Target.Key.Value ?? "-"}",
            NoticeSeverity.Critical,
            headline,
            $"{what}{more} Hosting a savegame on it may damage that save.",
            footnote: null,
            // No revision on this one's re-apply: the folder card beside it is the one that owns the
            // mod half, and two buttons in one column disagreeing about a number would be worse than
            // one of them being plainly worded.
            actions: ModActions(entry, repo, reviewLeads: canReview, canReview, environment: null),
            subject: Subject(entry, repo));
    }

    private static string Names(IEnumerable<DriftedLockedMod> mods)
    {
        var names = mods.Select(x => $"'{x.DisplayName}'").ToList();

        return names.Count == 1
            ? names[0]
            : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
    }


    /// <summary>
    /// One held savegame that has stopped agreeing with the repo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One card per savegame, with every kind that applies said in it.</b> The kinds are not
    /// exclusive - a save can have been played here <em>and</em> taken over by somebody who checked
    /// in, which is the worst case and the one where saying only half would be actively misleading -
    /// so they are not split into a card each. The headline is the first kind's, because
    /// <see cref="SavegameDriftRules"/> puts unchecked-in play first and that is the right one to
    /// lead with: it is the state where somebody's evening exists on this disk and nowhere else.
    /// </para>
    /// <para>
    /// <b>Its action is the saves list, not a check-in of its own.</b> Check in, discard and stop
    /// tracking all live on the repo's saves list beside the row this names; a card that grew its own
    /// copy of one of them would be a second door to a flow that has confirmations in it.
    /// </para>
    /// </remarks>
    private static Notice Savegame(
        IReadOnlyList<SavegameDrift> kinds,
        DriftCandidate game,
        NoticeRepo repo,
        INoticeEnvironment environment)
    {
        var first = kinds[0];
        var save = first.SlotDisplayName is { Length: > 0 } named ? $"'{named}'" : "A savegame checked out here";

        var headline = first.Kind switch
        {
            SavegameDriftKind.UncheckedInPlay => $"{save} holds play that exists nowhere else",
            SavegameDriftKind.TakenOverAndCheckedIn => $"{save} has been checked in by somebody else",
            _ => $"{save} is on the wrong mod list"
        };

        var actions = new List<NoticeAction>
        {
            new(NoticeActionKind.OpenSavegame, "Open the save") { IsPrimary = true }
        };

        // A past savegame pinned to a revision its folder has moved off is the one savegame problem a
        // re-apply actually fixes, and the button names the number it is going to install rather than
        // offering a latest the apply table refuses.
        if (kinds.Any(x => x.Kind is SavegameDriftKind.PlayedOnAnotherModList && x.RunsOnAnotherProfile is false)
            && game.ActiveProfile is ActiveProfile active)
        {
            actions.Add(new(
                NoticeActionKind.Reapply,
                environment.RequiredRevision(game.Identity, active.ProfileId) is int revision
                    ? $"Re-apply rev {revision}"
                    : "Re-apply now"));
        }

        return Make(
            $"save/{first.SavegameId}",
            NoticeSeverity.Critical,
            headline,
            string.Join(' ', kinds.Select(x => DescribeSavegame(x, save))),
            footnote: null,
            actions: actions,
            subject: new NoticeSubject(game.Identity, game.Name)
            {
                RepoId = first.RepoId,
                ProfileId = game.ActiveProfile?.ProfileId,
                SavegameId = first.SavegameId
            },
            membership: repo.MembershipLevel);
    }

    private static string DescribeSavegame(SavegameDrift drift, string save) => drift.Kind switch
    {
        SavegameDriftKind.UncheckedInPlay =>
            $"{save} has been played since it was checked out, and that play exists nowhere but this disk "
                + "until it is checked in.",

        SavegameDriftKind.TakenOverAndCheckedIn =>
            $"{save} has been checked in by somebody else - they are on snapshot {drift.HeadSnapshot}, this "
                + $"machine is holding snapshot {drift.HeldSnapshot}. Checking in from here forks it, and will "
                + "be refused unless you force it.",

        // Two sentences for one kind, because the rule reaches it two ways and only one of them is
        // about numbers. Against another profile entirely, putting the two revisions side by side
        // would be comparing integers belonging to different mod lists - which is the thing the
        // design refuses, said by the notice meant to explain it.
        SavegameDriftKind.PlayedOnAnotherModList when drift.RunsOnAnotherProfile =>
            $"{save} follows a different mod list from the one this mod folder is on. Playing it on the "
                + "wrong mod list is what damages a save.",

        SavegameDriftKind.PlayedOnAnotherModList =>
            $"{save} runs on revision {drift.TargetRevision} of its mod list and this mod folder is on "
                + $"revision {drift.AppliedRevision}. Playing it on the wrong mod list is what damages a save.",

        _ => $"{save} no longer agrees with what the repo holds."
    };


    /// <summary>
    /// The one notice here that is not about a game at all.
    /// </summary>
    /// <remarks>
    /// <b>Per volume, because the volume is the problem.</b> The mod it names is the symptom: the
    /// game's updater wrote through a hardlink into a cache every repo on that drive shares, so the
    /// blast radius is the drive. One card per corrupt blob would report a drive's worth of one
    /// mistake as a list of unrelated mods.
    /// </remarks>
    private static IEnumerable<Notice> ForStore(IReadOnlyList<CorruptedBlob> corruption)
    {
        foreach (var volume in corruption.GroupBy(x => x.VolumeRoot))
        {
            var found = volume.ToList();
            var first = found[0];

            var outcome = first.Removed
                ? "The bad copy has been dropped and will be downloaded again when something needs it."
                : "It could not be dropped - something still has the file open - and will be caught again "
                    + "on the next check.";

            var more = found.Count > 1 ? $" {found.Count - 1} more were found the same way." : "";

            yield return Make(
                $"store/{volume.Key}",
                NoticeSeverity.Critical,
                $"The game wrote into the mod cache on {volume.Key}",
                $"'{first.DisplayName}' was written straight into the cache, which every repo on that drive "
                    + $"shares. {outcome}{more}",
                footnote: null,
                actions: [new(NoticeActionKind.OpenLog, "Open log folder")],
                subject: null);
        }
    }


    /// <summary>
    /// A game that has drifted and that this account cannot do anything about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two causes, and only one is worth alarming anybody about.</b> Before the repo list has been
    /// read this is a shell that started a moment ago, and the notice says so and waits. After a
    /// successful read it is a game connected to a repo this account was removed from, left, or
    /// archived - a standing state somebody has to be told about, because nothing else in the app
    /// mentions it.
    /// </para>
    /// <para>
    /// <b>Not dismissible, and it offers nothing.</b> It reports a state rather than an event, so
    /// waving it away would not make it less true; and it cannot offer the way out, because
    /// disconnecting a game is reached through the very repo that is gone. See
    /// docs/08-known-issues.md.
    /// </para>
    /// </remarks>
    private static Notice Unreachable(string gameName, GameIdentity game, bool reposLoaded)
    {
        return Make(
            $"unreachable/{game}",
            NoticeSeverity.Warning,
            reposLoaded
                ? $"'{gameName}' has drifted, and it belongs to a repo you are not in"
                : $"'{gameName}' has drifted",
            reposLoaded
                ? "Its mod folders still follow a profile in a repo this account cannot see - it was left, "
                    + "or the membership was removed, or the repo was archived. Nothing here can put them right "
                    + "or say what changed, because everything that would is behind that repo. Sign in as "
                    + "somebody who is in it, or ask to be let back in."
                : "Checking which repo it belongs to...",
            footnote: null,
            actions: [],
            subject: new NoticeSubject(game, gameName),
            canDismiss: false);
    }


    /// <summary>
    /// The two mod actions, in the order they should be offered.
    /// </summary>
    /// <param name="environment">
    /// Null where the caller does not want a revision on the re-apply - the locked card, whose
    /// re-apply is the same one the folder card beside it offers.
    /// </param>
    private static IReadOnlyList<NoticeAction> ModActions(
        TargetDrift entry,
        NoticeRepo repo,
        bool reviewLeads,
        bool canReview,
        INoticeEnvironment? environment)
    {
        var game = entry.Game;
        var actions = new List<NoticeAction>();

        if (canReview)
        {
            actions.Add(new(NoticeActionKind.Review, "Review") { IsPrimary = reviewLeads });
        }

        // Re-applying needs somewhere to apply to. A game reported purely because it is holding a
        // savegame may follow no profile at all, and a button that returns the moment it is pressed
        // is worse than no button.
        if (game.ActiveProfile is ActiveProfile active && entry.Target is not null)
        {
            var label = environment?.RequiredRevision(game.Identity, active.ProfileId) is int revision
                ? $"Re-apply rev {revision}"
                : "Re-apply now";

            actions.Add(new(NoticeActionKind.Reapply, label) { IsPrimary = reviewLeads is false });
        }

        return actions;
    }

    /// <summary>
    /// Whether reviewing is a thing this user can do at all.
    /// </summary>
    /// <remarks>
    /// A guest cannot import, and the mod list they would land on is the read-only one - so the
    /// button is not offered rather than offered and refused, and the prompt that sends them there is
    /// not written either. Re-applying is theirs, which is why their card still has something to do.
    /// </remarks>
    private static bool CanReview(TargetDrift entry, NoticeRepo repo)
        => entry.Game.ActiveProfile is not null
            && entry.Target is not null
            && repo.MembershipLevel >= RepoMembershipLevel.Member;

    private static NoticeSubject Subject(TargetDrift entry, NoticeRepo repo) =>
        new(entry.Game.Identity, entry.Game.Name)
        {
            Target = entry.Target?.Target,
            RepoId = repo.Id,
            ProfileId = entry.Game.ActiveProfile?.ProfileId,
            ProfileName = entry.ProfileName
        };

    /// <summary>
    /// Which of the game's folders this entry is about, or null where there is nothing to tell apart
    /// - one folder, or an entry that is about the game rather than a folder of it.
    /// </summary>
    private static string? FolderName(TargetDrift entry, IReadOnlyDictionary<TargetKey, string> folders)
        => entry.Target is GameModFolder target
            ? TargetNames.Distinguishing(
                target.Target.Key,
                folders.GetValueOrDefault(target.Target.Key),
                entry.Game.Targets.Count)
            : null;


    /// <summary>
    /// Builds one notice and signs it with everything it says.
    /// </summary>
    /// <remarks>
    /// The signature is the rendered content rather than the facts behind it, and deliberately: what
    /// a dismissal means is "I have read this", so the thing that has to invalidate it is the text
    /// changing. A fact that moves without changing a word of the card is not news.
    /// </remarks>
    private static Notice Make(
        string key,
        NoticeSeverity severity,
        string headline,
        string? body,
        string? footnote,
        IReadOnlyList<NoticeAction> actions,
        NoticeSubject? subject,
        bool canDismiss = true,
        RepoMembershipLevel? membership = null)
    {
        var signature = string.Join(SignatureSeparator, headline, body, footnote);

        return new Notice(key, signature, severity, headline)
        {
            Body = string.IsNullOrWhiteSpace(body) ? null : body,
            Footnote = footnote,
            Actions = actions,
            Subject = subject,
            CanDismiss = canDismiss,
            Membership = membership
        };
    }


    /// <summary>
    /// By what is at stake, keeping a game's notices together.
    /// </summary>
    /// <remarks>
    /// A game sorts by its worst notice rather than each card sorting alone, so the folders of one
    /// game stay adjacent and can be drawn under one heading. Sorting every card independently would
    /// put a game's locked mods at the top of the column and its folder drift six cards below, which
    /// reads as two unrelated problems.
    /// </remarks>
    private static IReadOnlyList<Notice> Sort(IReadOnlyList<Notice> notices)
    {
        var worst = notices
            .GroupBy(Group)
            .ToDictionary(x => x.Key, x => x.Min(n => n.Severity));

        return
        [
            .. notices
                .OrderBy(x => worst[Group(x)])
                .ThenBy(Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Severity)
        ];
    }

    /// <summary>What sorts together: the game, or the notice itself where it belongs to none.</summary>
    private static string Group(Notice notice)
        => notice.Subject is NoticeSubject subject ? subject.Game.ToString() : notice.Key;
}
