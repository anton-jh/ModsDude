using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Notices;
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
        Assert.Contains("1 replaced", notice.Body);

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

        Assert.Equal(NoticeSeverity.Pending, notice.Severity);
        Assert.Equal(NoticeActionKind.Reapply, notice.Actions.Single(x => x.IsPrimary).Kind);
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

        Assert.Contains("'Big Map' is locked at 1.4.2 and is no longer installed", locked.Headline);
        Assert.Contains("Hosting a savegame on it may damage that save.", locked.Body);
    }

    /// <summary>
    /// Twenty locked mods are one update-all with one remedy. They are named in the body rather than
    /// drawn as twenty cards, which is the wall the single card was right to be afraid of.
    /// </summary>
    [Fact]
    public void Many_locked_mods_stay_one_notice_and_name_the_first_few()
    {
        var notices = Build(Drifted(
            changed: ["a.zip"],
            locked:
            [
                new(ModKey.From("a"), "Alpha", null, LockedDriftReason.FileChanged),
                new(ModKey.From("b"), "Bravo", null, LockedDriftReason.FileChanged),
                new(ModKey.From("c"), "Charlie", null, LockedDriftReason.FileChanged),
                new(ModKey.From("d"), "Delta", null, LockedDriftReason.FileChanged),
                new(ModKey.From("e"), "Echo", null, LockedDriftReason.FileChanged)
            ]));

        var locked = Assert.Single(notices, x => x.Key.StartsWith("locked/"));

        Assert.Equal("5 locked mods in 'Farming Simulator 25' are not what they were locked at", locked.Headline);
        Assert.Contains("'Bravo', 'Charlie' and 'Delta'", locked.Body);
        Assert.Contains("1 more", locked.Body);
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

        Assert.Contains(notices, x => x.Headline.Contains("holds play that exists nowhere else"));
        Assert.Contains(notices, x => x.Headline.Contains("has been checked in by somebody else"));
        Assert.Contains(notices, x => x.Headline.Contains("is on the wrong mod list"));

        // Check in, discard and stop tracking all live on the repo's saves list beside the row.
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

        Assert.Contains("has been played since it was checked out", notice.Body);
        Assert.Contains("has been checked in by somebody else", notice.Body);
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

        Assert.Contains(notice.Actions, x => x.Label == "Re-apply rev 4");
        Assert.Contains("runs on revision 4", notice.Body);
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
        Assert.Contains("follows a different mod list", notice.Body);
    }

    /// <summary>
    /// A store is shared by every repo on its volume, so the mod it names is the symptom and the
    /// drive is the problem. It was a line inside a card headlined with a game's name.
    /// </summary>
    [Fact]
    public void The_shared_cache_is_its_own_notice_per_volume()
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

        Assert.Equal(@"The game wrote into the mod cache on D:\", d.Headline);
        Assert.Contains("1 more were found the same way.", d.Body);
    }

    /// <summary>
    /// Everything the column can offer goes through the repo, so a game whose repo this account
    /// cannot see gets one card that says exactly that and offers nothing.
    /// </summary>
    [Fact]
    public void A_game_whose_repo_is_gone_says_so_and_offers_nothing()
    {
        var notices = Build(
            Drifted(changed: ["fs25_a.zip"]),
            environment: new FakeEnvironment { Repo = null });

        var notice = Assert.Single(notices);

        Assert.Contains("it belongs to a repo you are not in", notice.Headline);
        Assert.Empty(notice.Actions);

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

        Assert.Equal("'Farming Simulator 25' has drifted", notice.Headline);
        Assert.Equal("Checking which repo it belongs to...", notice.Body);
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
    /// The drifted files are by definition versions the user now has and the repo may not, so the
    /// warning doubles as the first step of the flow they came back to perform anyway.
    /// </summary>
    [Fact]
    public void A_drifted_folder_doubles_as_an_import_prompt()
    {
        var notice = Assert.Single(Build(Drifted(changed: ["fs25_a.zip"])));

        Assert.Contains("Opening the mod list is where they get imported.", notice.Footnote);
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
                new DriftReport(DriftStatus.Drifted, [], [], changed, []) with
                {
                    LockedDrift = locked ?? []
                },
                target,
                game,
                name)[0]
        ];
    }

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
