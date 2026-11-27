using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Notices;
using ModsDude.Client.Core.Profiles.Editor;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Notices;

/// <summary>
/// What the column says, which is the half of the old single drift card that no test ever reached.
/// </summary>
public class NoticeBuilderTests
{
    private static readonly GameIdentity _fs25 = new("fs25");
    private static readonly GameIdentity _beamng = new("beamng");
    private static readonly Guid _repoId = Guid.NewGuid();
    private static readonly Guid _profileId = Guid.NewGuid();


    [Fact]
    public void A_drifted_folder_is_one_notice_that_leads_with_review()
    {
        var notices = Build(Drifted(changed: ["fs25_a.zip"]));

        var notice = Assert.Single(notices);

        Assert.Equal(NoticeSeverity.Warning, notice.Severity);
        Assert.Contains("'Farming Simulator 25' no longer matches 'Season 4'", notice.Headline);
        Assert.Equal([new NoticeFact("Wrong version", "1")], notice.Facts);
        Assert.Null(notice.Body);

        // What the folder holds now is usually what the user meant to end up with, and re-applying
        // before looking throws it away.
        Assert.True(notice.Actions[0].IsPrimary);
        Assert.Equal(NoticeActionKind.Review, notice.Actions[0].Kind);
        Assert.Equal(NoticeActionKind.Reapply, notice.Actions[1].Kind);
    }

    /// <summary>
    /// Nothing has been installed or removed yet, so there is nothing to look at first.
    /// </summary>
    [Fact]
    public void A_folder_that_never_got_its_apply_leads_with_re_apply()
    {
        var notices = Build(Entry(DriftReport.For(DriftStatus.NotApplied)));

        var notice = Assert.Single(notices);

        Assert.Equal(NoticeActionKind.Reapply, notice.Actions.Single(x => x.IsPrimary).Kind);
    }

    /// <summary>
    /// Each of these is a game that would be played on mods that may not be its profile's.
    /// </summary>
    [Theory]
    [InlineData(DriftStatus.NotApplied)]
    [InlineData(DriftStatus.FolderRepointed)]
    [InlineData(DriftStatus.NeverSynced)]
    public void A_folder_not_on_its_profile_is_a_warning(DriftStatus status)
    {
        var notice = Assert.Single(Build(Entry(DriftReport.For(status))));

        Assert.Equal(NoticeSeverity.Warning, notice.Severity);
    }

    /// <summary>
    /// A guest cannot import, so the page Review would open is the read-only mod list - which is not
    /// an answer to "your mod folder has drifted". Re-applying is the whole of what is theirs.
    /// </summary>
    [Fact]
    public void A_guest_is_offered_re_apply_and_nothing_it_cannot_do()
    {
        var notices = Build(
            Drifted(changed: ["fs25_a.zip"]),
            environment: new FakeEnvironment { Membership = RepoMembershipLevel.Guest });

        var notice = Assert.Single(notices);

        Assert.DoesNotContain(notice.Actions, x => x.Kind is NoticeActionKind.Review);
        Assert.Equal(NoticeActionKind.Reapply, notice.Actions.Single().Kind);
        Assert.True(notice.Actions.Single().IsPrimary);

        // And the prompt that would have sent them there is not written either.
        Assert.Null(notice.Footnote);
    }

    /// <summary>
    /// A locked map at the wrong version is a damaged savegame waiting to happen, and the single card
    /// said so on a line inside the folder's own warning. It is its own card now, because its
    /// severity is not the folder's.
    /// </summary>
    [Fact]
    public void Locked_mods_are_a_separate_critical_notice_from_the_folder_they_are_in()
    {
        var notices = Build(Drifted(
            changed: ["bigmap.zip"],
            locked: [new(ModKey.From("bigmap"), "Big Map", "1.4.2", LockedDriftReason.FileRemoved)]));

        Assert.Equal(2, notices.Count);

        var locked = notices[0];
        var folder = notices[1];

        // Critical sorts above the warning it came with.
        Assert.Equal(NoticeSeverity.Critical, locked.Severity);
        Assert.Equal(NoticeSeverity.Warning, folder.Severity);

        Assert.Equal("'Big Map' is locked at 1.4.2 and is no longer installed in the 'mods' folder", locked.Headline);
        Assert.Equal([new NoticeFact("Big Map", "Missing")], locked.Facts);
        Assert.Equal("The wrong version can damage a savegame.", locked.Body);
    }

    /// <summary>
    /// Twenty locked mods are one update-all with one remedy, so they stay one card and list the
    /// first few by name.
    /// </summary>
    [Fact]
    public void Many_locked_mods_stay_one_notice_and_list_the_first_few()
    {
        var notices = Build(Drifted(
            changed: ["a.zip"],
            locked:
            [
                new(ModKey.From("f"), "Foxtrot", null, LockedDriftReason.FileChanged),
                new(ModKey.From("b"), "Bravo", null, LockedDriftReason.FileRemoved),
                new(ModKey.From("a"), "Alpha", null, LockedDriftReason.FileChanged),
                new(ModKey.From("e"), "Echo", null, LockedDriftReason.FileChanged),
                new(ModKey.From("c"), "Charlie", null, LockedDriftReason.FileRemoved),
                new(ModKey.From("d"), "Delta", null, LockedDriftReason.FileChanged)
            ]));

        var locked = Assert.Single(notices, x => x.Key.StartsWith("locked/"));

        Assert.Equal("6 locked mods in 'Farming Simulator 25' do not match their lock in the 'mods' folder", locked.Headline);
        Assert.Equal(
            [
                new NoticeFact("Alpha", "Wrong version"),
                new NoticeFact("Bravo", "Missing"),
                new NoticeFact("Charlie", "Missing"),
                new NoticeFact("Delta", "Wrong version"),
                new NoticeFact("More", "2")
            ],
            locked.Facts);
    }

    [Fact]
    public void Five_locked_mods_are_all_listed()
    {
        var notices = Build(Drifted(
            changed: ["a.zip"],
            locked: [.. "abcde".Select(x => new DriftedLockedMod(ModKey.From(x.ToString()), x.ToString(), null, LockedDriftReason.FileChanged))]));

        var locked = Assert.Single(notices, x => x.Key.StartsWith("locked/"));

        Assert.Equal(["a", "b", "c", "d", "e"], locked.Facts.Select(x => x.Label));
    }

    [Fact]
    public void A_drifted_folder_lists_every_kind_of_difference_in_a_fixed_order()
    {
        var report = new DriftReport(DriftStatus.Drifted, ["new.zip"], ["gone1.zip", "gone2.zip"], ["moved.zip"])
        {
            AppliedRevision = 12,
            CurrentRevision = 14,
            EditedGameFiles = ["mods.xml"]
        };

        var notice = Assert.Single(Build(Entry(report)));

        Assert.Equal(
            [
                new NoticeFact("Missing", "2"),
                new NoticeFact("Wrong version", "1"),
                new NoticeFact("Added", "1"),
                new NoticeFact("Revision", "12 → 14"),
                new NoticeFact("Game files edited", "1")
            ],
            notice.Facts);
    }

    /// <summary>
    /// Somebody saved the profile and the folder is exactly what was applied: there is nothing on
    /// disk to look at, so updating leads and Review shows what changed between the two revisions.
    /// </summary>
    [Fact]
    public void A_folder_that_is_only_behind_its_profile_leads_with_update()
    {
        var notice = Assert.Single(Build(Behind(applied: 3, current: 6)));

        Assert.Equal(NoticeSeverity.Warning, notice.Severity);
        Assert.Equal("'Farming Simulator 25' is behind 'Season 4' in the 'mods' folder", notice.Headline);
        Assert.Equal([new NoticeFact("Applied", "Revision 3"), new NoticeFact("Current", "Revision 6")], notice.Facts);
        Assert.Null(notice.Body);
        Assert.Null(notice.Footnote);

        Assert.Equal(
            [
                new NoticeAction(NoticeActionKind.Review, "Review"),
                new NoticeAction(NoticeActionKind.Reapply, "Update to revision 6") { IsPrimary = true }
            ],
            notice.Actions);

        Assert.Equal(new NoticeReview.RevisionChanges(new RevisionGap(3, 6)), notice.Subject?.Review);
    }

    /// <summary>
    /// A savegame in compatibility mode can hold the game on an older revision than the one applied,
    /// and going back is not an update.
    /// </summary>
    [Fact]
    public void A_folder_ahead_of_the_revision_it_needs_offers_re_apply()
    {
        var notice = Assert.Single(Build(Behind(applied: 6, current: 4)));

        Assert.Equal("Re-apply revision 4", notice.Actions.Single(x => x.IsPrimary).Label);
        Assert.Equal(new NoticeReview.RevisionChanges(new RevisionGap(6, 4)), notice.Subject?.Review);
    }

    /// <summary>A profile's history is readable at Guest, so reviewing revision changes is theirs too.</summary>
    [Fact]
    public void A_guest_can_review_what_changed_between_revisions()
    {
        var notice = Assert.Single(Build(
            Behind(applied: 3, current: 6),
            environment: new FakeEnvironment { Membership = RepoMembershipLevel.Guest }));

        Assert.Contains(notice.Actions, x => x.Kind is NoticeActionKind.Review && x.IsPrimary is false);
        Assert.Equal(NoticeActionKind.Reapply, notice.Actions.Single(x => x.IsPrimary).Kind);
    }

    [Fact]
    public void A_folder_behind_its_profile_with_changes_on_disk_leads_with_review_of_the_mod_list()
    {
        var report = new DriftReport(DriftStatus.Drifted, [], [], ["fs25_a.zip"]) with { AppliedRevision = 3, CurrentRevision = 4 };

        var notice = Assert.Single(Build(Entry(report)));

        Assert.Equal(NoticeSeverity.Warning, notice.Severity);
        Assert.Contains("no longer matches", notice.Headline);
        Assert.Contains(new NoticeFact("Revision", "3 → 4"), notice.Facts);
        Assert.Equal(NoticeActionKind.Review, notice.Actions.Single(x => x.IsPrimary).Kind);
        Assert.Equal("Re-apply now", notice.Actions.Single(x => x.Kind is NoticeActionKind.Reapply).Label);
        Assert.Equal(new NoticeReview.ModList(PinnedModFilter.NotInSources), notice.Subject?.Review);
    }

    [Fact]
    public void A_repointed_folder_names_the_folder()
    {
        var notice = Assert.Single(Build(Entry(DriftReport.For(DriftStatus.FolderRepointed))));

        Assert.Equal([new NoticeFact("Folder", @"C:\games\mods")], notice.Facts);
    }

    [Fact]
    public void Reviewing_a_folder_the_game_only_updated_shows_updates()
    {
        var notice = Assert.Single(Build(Drifted(changed: ["fs25_a.zip"])));

        Assert.Equal(new NoticeReview.ModList(PinnedModFilter.Updates), notice.Subject?.Review);
    }

    [Fact]
    public void Reviewing_a_folder_with_anything_but_updates_shows_what_is_not_in_the_sources()
    {
        var report = new DriftReport(DriftStatus.Drifted, ["new.zip"], [], ["fs25_a.zip"]);

        var notice = Assert.Single(Build(Entry(report)));

        Assert.Equal(new NoticeReview.ModList(PinnedModFilter.NotInSources), notice.Subject?.Review);
    }

    /// <summary>
    /// Locked pins never show under Updates, and one whose file changed is still offered by the
    /// folder, so neither filter would show it.
    /// </summary>
    [Fact]
    public void Reviewing_locked_mods_shows_the_locked_ones()
    {
        var notices = Build(Drifted(
            changed: ["bigmap.zip"],
            locked: [new(ModKey.From("bigmap"), "Big Map", "1.4.2", LockedDriftReason.FileChanged)]));

        Assert.Equal(new NoticeReview.ModList(PinnedModFilter.Locked), notices.Single(x => x.Key.StartsWith("locked/")).Subject?.Review);
        Assert.Equal(new NoticeReview.ModList(PinnedModFilter.NotInSources), notices.Single(x => x.Key.StartsWith("drift/")).Subject?.Review);
    }

    /// <summary>
    /// The tail the single card closed its savegame line with - "2 more savegame problems here as
    /// well" - was three slots with three different remedies flattened into a sentence.
    /// </summary>
    [Fact]
    public void Every_drifted_savegame_gets_its_own_card()
    {
        var notices = Build(Entry(
            DriftReport.For(DriftStatus.InSync) with
            {
                SavegameDrift =
                [
                    Save("one", SavegameDriftKind.UncheckedInPlay),
                    Save("two", SavegameDriftKind.TakenOverAndCheckedIn),
                    Save("three", SavegameDriftKind.PlayedOnAnotherModList)
                ]
            }));

        Assert.Equal(3, notices.Count);
        Assert.All(notices, x => Assert.Equal(NoticeSeverity.Critical, x.Severity));

        Assert.Contains(notices, x => x.Headline.Contains("has progress that is not checked in"));
        Assert.Contains(notices, x => x.Headline.Contains("has been checked in by somebody else"));
        Assert.Contains(notices, x => x.Headline.Contains("does not match this mod folder"));

        // Check in, discard and stop tracking all live on the repo's savegames list beside the row.
        Assert.All(notices, x => Assert.Equal(NoticeActionKind.OpenSavegame, x.Actions[0].Kind));
    }

    /// <summary>
    /// The kinds are not exclusive, and a save that was played here <em>and</em> taken over is the
    /// worst case - the one where saying only half would be actively misleading.
    /// </summary>
    [Fact]
    public void One_save_in_two_states_is_one_card_saying_both()
    {
        var notices = Build(Entry(
            DriftReport.For(DriftStatus.InSync) with
            {
                SavegameDrift =
                [
                    Save("one", SavegameDriftKind.UncheckedInPlay, id: _savegameId),
                    Save("one", SavegameDriftKind.TakenOverAndCheckedIn, id: _savegameId)
                ]
            }));

        var notice = Assert.Single(notices);

        Assert.Contains("only on this machine until it is checked in", notice.Body);
        Assert.Contains("Their snapshot is", notice.Body);
    }

    [Fact]
    public void A_save_whose_repo_is_gone_offers_stop_tracking_and_its_game()
    {
        var notice = Assert.Single(Build(Entry(
            DriftReport.For(DriftStatus.DanglingProfile) with
            {
                SavegameDrift =
                [
                    Save("Testing stuff", SavegameDriftKind.RepoGone, id: _savegameId),
                    Save("Testing stuff", SavegameDriftKind.UncheckedInPlay, id: _savegameId)
                ]
            })));

        Assert.Equal("'Testing stuff' is from a repo you are not in", notice.Headline);
        Assert.Equal("It has progress that is not checked in.", notice.Body);
        Assert.Equal(NoticeBuilder.SavegameKey(_savegameId), notice.Key);

        Assert.Collection(
            notice.Actions,
            action =>
            {
                Assert.Equal(NoticeActionKind.StopTracking, action.Kind);
                Assert.True(action.IsPrimary);
            },
            action =>
            {
                Assert.Equal(NoticeActionKind.OpenGame, action.Kind);
                Assert.False(action.IsPrimary);
            });

        Assert.Equal(_savegameId, notice.Subject?.SavegameId);
        Assert.Equal(_repoId, notice.Subject?.RepoId);
        Assert.Equal("Testing stuff", notice.Subject?.SavegameName);
    }

    [Fact]
    public void An_unplayed_save_whose_repo_is_gone_still_gets_its_card()
    {
        var notice = Assert.Single(Build(Entry(
            DriftReport.For(DriftStatus.DanglingProfile) with
            {
                SavegameDrift = [Save("one", SavegameDriftKind.RepoGone)]
            })));

        Assert.Equal("'one' is from a repo you are not in", notice.Headline);
        Assert.Null(notice.Body);
    }

    /// <summary>Neither stopping tracking nor the game's page needs a repo, so a game this account cannot reach still gets both ways out.</summary>
    [Fact]
    public void A_save_whose_repo_is_gone_gets_its_card_when_no_repo_serves_the_game()
    {
        var notice = Assert.Single(Build(
            Entry(DriftReport.For(DriftStatus.NoActiveProfile) with
            {
                SavegameDrift = [Save("one", SavegameDriftKind.RepoGone)]
            }),
            environment: new FakeEnvironment { Repo = null }));

        Assert.Equal([NoticeActionKind.StopTracking, NoticeActionKind.OpenGame], notice.Actions.Select(x => x.Kind));
        Assert.Null(notice.GroupLabel);
    }

    [Fact]
    public void A_game_no_repo_serves_still_says_so_beside_a_gone_save_when_more_is_wrong()
    {
        var notices = Build(
            Entry(new DriftReport(DriftStatus.Drifted, [], [], ["a.zip"]) with
            {
                SavegameDrift = [Save("one", SavegameDriftKind.RepoGone)]
            }),
            environment: new FakeEnvironment { Repo = null });

        Assert.Equal(2, notices.Count);
        Assert.Contains(notices, x => x.Actions.Any(action => action.Kind is NoticeActionKind.StopTracking));
        Assert.Contains(notices, x => x.Headline == "'Farming Simulator 25' belongs to a repo you are not in");
        Assert.All(notices, x => Assert.Equal("Farming Simulator 25", x.GroupLabel));
    }

    /// <summary>
    /// A warning naming a person is the only kind anybody reads, and once they have it the copies have
    /// already forked - so the sentence is about which check-in wins, not about a fork still to come.
    /// </summary>
    [Fact]
    public void A_save_taken_over_names_who_took_it_and_what_checking_in_costs()
    {
        var notice = Assert.Single(Build(Entry(
            DriftReport.For(DriftStatus.InSync) with
            {
                SavegameDrift = [Save("one", SavegameDriftKind.TakenOver) with
                {
                    TakenBy = new SavegameClaimHolder("bob", "Bob", DateTime.UtcNow.AddHours(-2))
                }]
            })));

        Assert.Equal(NoticeSeverity.Critical, notice.Severity);
        Assert.Equal("'one' is now checked out to Bob", notice.Headline);
        Assert.Contains("Bob checked it out on", notice.Body);
        Assert.Contains("Whoever checks in second overwrites", notice.Body);
    }

    /// <summary>
    /// Taken and let go again: there is nobody to name, and nothing has been checked in over this
    /// machine's copy, so the card says the claim is gone and that checking in still works.
    /// </summary>
    [Fact]
    public void A_save_taken_over_and_let_go_says_the_claim_is_gone()
    {
        var notice = Assert.Single(Build(Entry(
            DriftReport.For(DriftStatus.InSync) with
            {
                SavegameDrift = [Save("one", SavegameDriftKind.TakenOver)]
            })));

        Assert.Equal("'one' is no longer checked out to you", notice.Headline);
        Assert.Contains("You can still check it in", notice.Body);
    }

    /// <summary>
    /// The one savegame problem a re-apply actually fixes, with the number it will install on the
    /// button rather than a latest the apply table refuses.
    /// </summary>
    [Fact]
    public void A_save_on_a_past_revision_offers_the_re_apply_that_fixes_it()
    {
        var notices = Build(
            Entry(DriftReport.For(DriftStatus.InSync) with
            {
                SavegameDrift = [Save("one", SavegameDriftKind.PlayedOnAnotherModList) with
                {
                    TargetRevision = 4,
                    AppliedRevision = 7
                }]
            }),
            environment: new FakeEnvironment { RequiredRevision = 4 });

        var notice = Assert.Single(notices);

        Assert.Contains(notice.Actions, x => x.Label == "Re-apply revision 4");
        Assert.Contains("needs revision 4", notice.Body);
    }

    /// <summary>
    /// Against another profile entirely, putting the two revisions side by side would compare
    /// integers belonging to different mod lists - and no re-apply reaches it.
    /// </summary>
    [Fact]
    public void A_save_on_another_profile_is_not_offered_a_re_apply()
    {
        var notices = Build(Entry(
            DriftReport.For(DriftStatus.InSync) with
            {
                SavegameDrift = [Save("one", SavegameDriftKind.PlayedOnAnotherModList) with
                {
                    RunsOnAnotherProfile = true
                }]
            }));

        var notice = Assert.Single(notices);

        Assert.DoesNotContain(notice.Actions, x => x.Kind is NoticeActionKind.Reapply);
        Assert.Contains("uses a different profile", notice.Body);
    }

    /// <summary>
    /// A store is shared by every repo on its volume, so the mod it names is the symptom and the
    /// drive is the problem. It was a line inside a card headlined with a game's name.
    /// </summary>
    [Fact]
    public void The_shared_store_is_its_own_notice_per_volume()
    {
        var notices = Build(
            [],
            corruption:
            [
                new(ModKey.From("a"), "Alpha", "hash-a", "a.zip", @"D:\", Removed: true),
                new(ModKey.From("b"), "Bravo", "hash-b", "b.zip", @"D:\", Removed: true),
                new(ModKey.From("c"), "Charlie", "hash-c", "c.zip", @"E:\", Removed: false)
            ]);

        Assert.Equal(2, notices.Count);
        Assert.All(notices, x => Assert.Equal(NoticeSeverity.Critical, x.Severity));
        Assert.All(notices, x => Assert.Null(x.Subject));

        var d = Assert.Single(notices, x => x.Key == @"store/D:\");

        Assert.Equal(@"A game changed a mod in the store on D:\", d.Headline);
        Assert.Contains("1 more were affected.", d.Body);
    }

    /// <summary>
    /// Everything else the column can offer goes through the repo, so a game whose repo this account
    /// cannot see gets one card that says exactly that and leads to the game's page, which needs none.
    /// </summary>
    [Fact]
    public void A_game_whose_repo_is_gone_says_so_and_offers_its_page()
    {
        var notices = Build(
            Drifted(changed: ["fs25_a.zip"]),
            environment: new FakeEnvironment { Repo = null });

        var notice = Assert.Single(notices);

        Assert.Equal("'Farming Simulator 25' belongs to a repo you are not in", notice.Headline);

        var action = Assert.Single(notice.Actions);
        Assert.Equal(NoticeActionKind.OpenGame, action.Kind);
        Assert.True(action.IsPrimary);

        // It reports a state rather than an event: waving it away would not make it less true, and
        // nothing else in the app mentions it.
        Assert.False(notice.CanDismiss);
    }

    /// <summary>
    /// An empty repo list means "not asked yet" for the second between the shell appearing and the
    /// first fetch landing. Accusing a healthy startup of a lost membership would be inventing a
    /// state.
    /// </summary>
    [Fact]
    public void Before_the_repos_have_loaded_it_waits_rather_than_accusing()
    {
        var notices = Build(
            Drifted(changed: ["fs25_a.zip"]),
            environment: new FakeEnvironment { Repo = null, ReposLoaded = false });

        var notice = Assert.Single(notices);

        Assert.Equal("'Farming Simulator 25' does not match its profile", notice.Headline);
        Assert.Equal("Checking which repo it belongs to…", notice.Body);
        Assert.Empty(notice.Actions);
    }

    /// <summary>
    /// A machine running a dedicated server and an MP client contributes several cards for one
    /// alt-tab. They stay together under the game's name, or three folders read as three games.
    /// </summary>
    [Fact]
    public void A_games_folders_stay_together_under_one_heading()
    {
        var notices = Build(
        [
            Drifted(changed: ["a.zip"], target: "mods")[0],
            Drifted(changed: ["b.zip"], target: "server")[0]
        ]);

        Assert.Equal(2, notices.Count);
        Assert.All(notices, x => Assert.Equal("Farming Simulator 25", x.GroupLabel));

        // And the folder is in the headline now that a headline is about one of them.
        Assert.Contains(notices, x => x.Headline.Contains("in the 'server' folder"));
    }

    /// <summary>
    /// One card for a game names the game in its own headline, so a heading repeating it would be
    /// furniture.
    /// </summary>
    [Fact]
    public void A_game_with_one_notice_gets_no_heading()
    {
        var notice = Assert.Single(Build(Drifted(changed: ["a.zip"])));

        Assert.Null(notice.GroupLabel);
    }

    /// <summary>
    /// The column is read top down by somebody who has just come back from the game, and the only
    /// ordering that respects that puts what can cost them an evening above what costs them a tidy
    /// folder. Games sort by their worst card and stay together.
    /// </summary>
    [Fact]
    public void The_game_with_something_at_stake_sorts_above_the_one_that_is_merely_untidy()
    {
        var notices = Build(
        [
            .. Drifted(changed: ["a.zip"], game: _beamng, name: "BeamNG.drive"),
            .. Drifted(
                changed: ["bigmap.zip"],
                locked: [new(ModKey.From("bigmap"), "Big Map", null, LockedDriftReason.FileRemoved)])
        ]);

        Assert.Equal(3, notices.Count);

        // Farming Simulator's two cards first, because one of them is critical - and its warning
        // comes with it rather than being stranded below BeamNG's.
        Assert.Equal("Farming Simulator 25", notices[0].GroupLabel);
        Assert.Equal(NoticeSeverity.Critical, notices[0].Severity);
        Assert.Equal("Farming Simulator 25", notices[1].GroupLabel);
        Assert.Contains("BeamNG.drive", notices[2].Headline);
    }

    /// <summary>
    /// The drifted files are versions the user now has and the repo may not, so the warning doubles
    /// as an import prompt.
    /// </summary>
    [Fact]
    public void A_drifted_folder_doubles_as_an_import_prompt()
    {
        var notice = Assert.Single(Build(Drifted(changed: ["fs25_a.zip"])));

        Assert.Equal("Versions now on disk can be imported from the mod list.", notice.Footnote);
    }

    [Fact]
    public void A_folder_that_only_lost_mods_has_nothing_to_import()
    {
        var notice = Assert.Single(Build(Entry(new DriftReport(DriftStatus.Drifted, [], ["gone.zip"], []))));

        Assert.Null(notice.Footnote);
    }

    /// <summary>
    /// A dismissal is recorded against what the card says, so a third stray mod has to change it.
    /// </summary>
    [Fact]
    public void A_notice_is_signed_with_what_it_says()
    {
        var one = Assert.Single(Build(Drifted(changed: ["a.zip"])));
        var two = Assert.Single(Build(Drifted(changed: ["a.zip", "b.zip"])));

        Assert.Equal(one.Key, two.Key);
        Assert.NotEqual(one.Signature, two.Signature);
    }

    [Fact]
    public void A_count_moving_from_one_row_to_another_changes_the_signature()
    {
        var replaced = Assert.Single(Build(Entry(new DriftReport(DriftStatus.Drifted, [], [], ["a.zip"]))));
        var missing = Assert.Single(Build(Entry(new DriftReport(DriftStatus.Drifted, [], ["a.zip"], []))));

        Assert.NotEqual(replaced.Signature, missing.Signature);
    }


    private static readonly Guid _savegameId = Guid.NewGuid();

    private static IReadOnlyList<Notice> Build(
        IReadOnlyList<TargetDrift> drifted,
        IReadOnlyList<CorruptedBlob>? corruption = null,
        FakeEnvironment? environment = null)
        => NoticeBuilder.Build(drifted, corruption ?? [], environment ?? new FakeEnvironment());

    private static IReadOnlyList<TargetDrift> Drifted(
        IReadOnlyList<string> changed,
        IReadOnlyList<DriftedLockedMod>? locked = null,
        string target = "mods",
        GameIdentity? game = null,
        string? name = null)
    {
        return
        [
            Entry(
                new DriftReport(DriftStatus.Drifted, [], [], changed) with
                {
                    LockedDrift = locked ?? []
                },
                target,
                game,
                name)[0]
        ];
    }

    private static IReadOnlyList<TargetDrift> Behind(int applied, int current)
        => Entry(DriftReport.For(DriftStatus.Drifted) with { AppliedRevision = applied, CurrentRevision = current });

    private static IReadOnlyList<TargetDrift> Entry(
        DriftReport report,
        string target = "mods",
        GameIdentity? game = null,
        string? name = null)
    {
        var identity = game ?? _fs25;

        // Two targets so that the folder gets named at all - TargetNames says nothing about a game
        // that reaches one, which is nearly every game.
        var targets = new List<GameModFolder>
        {
            new(new(identity, new("mods")), @"C:\games\mods"),
            new(new(identity, new("server")), @"C:\games\server")
        };

        return
        [
            new TargetDrift(
                new DriftCandidate(identity, name ?? "Farming Simulator 25", targets, new(_repoId, _profileId)),
                targets.Single(x => x.Target.Key.Value == target),
                report,
                "Season 4")
        ];
    }

    private static SavegameDrift Save(string slot, SavegameDriftKind kind, Guid? id = null)
        => new(_repoId, id ?? Guid.NewGuid(), new(new("mods"), new(slot)), kind)
        {
            SlotDisplayName = slot,
            HeldSnapshot = 4,
            HeadSnapshot = 5
        };


    private sealed class FakeEnvironment : INoticeEnvironment
    {
        public bool ReposLoaded { get; init; } = true;
        public NoticeRepo? Repo { get; init; } = new(_repoId, RepoMembershipLevel.Member);
        public RepoMembershipLevel? Membership { get; init; }
        public int? RequiredRevision { get; init; }


        NoticeRepo? INoticeEnvironment.FindRepo(GameIdentity game, Guid? profileRepoId)
            => Membership is RepoMembershipLevel level ? new(_repoId, level) : Repo;

        IReadOnlyDictionary<TargetKey, string> INoticeEnvironment.FolderNames(GameIdentity game)
            => new Dictionary<TargetKey, string>
            {
                [new("mods")] = "mods",
                [new("server")] = "server"
            };

        int? INoticeEnvironment.RequiredRevision(GameIdentity game, Guid profileId) => RequiredRevision;
    }
}
