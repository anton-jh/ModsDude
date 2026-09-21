using ModsDude.Client.Core.Notices;

namespace ModsDude.Client.Core.Tests.Notices;

public class DriftToastPlannerTests
{
    private static Notice Make(string key, NoticeSeverity severity, string headline = "Headline", string? body = "Body.")
        => new(key, $"{headline}|{body}", severity, headline) { Body = body };

    private static DriftToast? Observe(
        DriftToastPlanner planner,
        IReadOnlyList<Notice> live,
        bool reposLoaded = true,
        bool windowInFront = false)
        => planner.Observe(live, reposLoaded, windowInFront);


    [Theory]
    [InlineData(NoticeSeverity.Critical)]
    [InlineData(NoticeSeverity.Warning)]
    public void Critical_and_warning_notices_are_worth_a_toast(NoticeSeverity severity)
    {
        var toast = Observe(new DriftToastPlanner(), [Make("a", severity)]);

        Assert.NotNull(toast);
    }

    [Theory]
    [InlineData(NoticeSeverity.Pending)]
    [InlineData(NoticeSeverity.Info)]
    public void Work_that_is_owed_and_absorbed_failures_are_left_to_the_column(NoticeSeverity severity)
    {
        Assert.Null(Observe(new DriftToastPlanner(), [Make("a", severity)]));
    }

    [Fact]
    public void A_pending_notice_beside_a_real_one_is_not_mentioned()
    {
        var toast = Observe(
            new DriftToastPlanner(),
            [Make("a", NoticeSeverity.Warning, "Real"), Make("b", NoticeSeverity.Pending, "Owed")]);

        Assert.Equal("Real", toast!.Title);
    }

    [Fact]
    public void Nothing_fires_while_the_user_is_looking_at_the_window()
    {
        Assert.Null(Observe(new DriftToastPlanner(), [Make("a", NoticeSeverity.Critical)], windowInFront: true));
    }

    [Fact]
    public void What_appeared_while_the_window_was_in_front_is_seen_and_does_not_become_news_later()
    {
        var planner = new DriftToastPlanner();
        var live = new[] { Make("a", NoticeSeverity.Critical) };

        Observe(planner, live, windowInFront: true);

        Assert.Null(Observe(planner, live, windowInFront: false));
    }

    [Fact]
    public void A_notice_is_announced_once()
    {
        var planner = new DriftToastPlanner();
        var live = new[] { Make("a", NoticeSeverity.Critical) };

        Assert.NotNull(Observe(planner, live));
        Assert.Null(Observe(planner, live));
        Assert.Null(Observe(planner, live));
    }

    [Fact]
    public void The_same_problem_saying_something_new_is_not_a_new_toast()
    {
        var planner = new DriftToastPlanner();

        Observe(planner, [Make("a", NoticeSeverity.Warning, body: "3 replaced.")]);

        Assert.Null(Observe(planner, [Make("a", NoticeSeverity.Warning, body: "4 replaced.")]));
    }

    [Fact]
    public void A_problem_that_went_away_and_came_back_is_news_again()
    {
        var planner = new DriftToastPlanner();
        var live = new[] { Make("a", NoticeSeverity.Critical) };

        Observe(planner, live);
        Observe(planner, []);

        Assert.NotNull(Observe(planner, live));
    }

    [Fact]
    public void A_new_notice_brings_a_toast_that_still_lists_the_old_ones()
    {
        var planner = new DriftToastPlanner();

        Observe(planner, [Make("a", NoticeSeverity.Warning, "First")]);

        var toast = Observe(
            planner,
            [Make("a", NoticeSeverity.Warning, "First"), Make("b", NoticeSeverity.Critical, "Second")]);

        Assert.Equal("2 things need attention", toast!.Title);
        Assert.Contains("First", toast.Body);
        Assert.Contains("Second", toast.Body);
        Assert.Null(toast.NoticeKey);
    }

    [Fact]
    public void A_toast_about_one_notice_carries_its_key_so_a_click_can_go_there()
    {
        var toast = Observe(new DriftToastPlanner(), [Make("drift/x", NoticeSeverity.Warning, "Only")]);

        Assert.Equal("drift/x", toast!.NoticeKey);
        Assert.Equal("Only", toast.Title);
    }

    [Fact]
    public void The_worst_notice_is_listed_first()
    {
        var toast = Observe(
            new DriftToastPlanner(),
            [Make("a", NoticeSeverity.Warning, "Mild"), Make("b", NoticeSeverity.Critical, "Severe")]);

        Assert.StartsWith("Severe", toast!.Body);
    }

    [Fact]
    public void A_long_list_is_cut_and_says_how_many_more()
    {
        var live = Enumerable.Range(1, 5).Select(x => Make($"k{x}", NoticeSeverity.Warning, $"Notice {x}")).ToList();

        var toast = Observe(new DriftToastPlanner(), live);

        Assert.Equal("5 things need attention", toast!.Title);
        Assert.EndsWith("and 2 more", toast.Body);
        Assert.Equal(4, toast.Body.Split('\n').Length);
    }

    [Fact]
    public void The_start_up_state_of_an_unread_repo_list_is_not_announced()
    {
        var planner = new DriftToastPlanner();

        Assert.Null(Observe(planner, [Make("unreachable/g", NoticeSeverity.Warning, "'G' has drifted")], reposLoaded: false));
    }

    [Fact]
    public void It_is_announced_once_the_repo_list_has_loaded_and_it_is_still_true()
    {
        var planner = new DriftToastPlanner();
        var live = new[] { Make("unreachable/g", NoticeSeverity.Warning, "'G' has drifted") };

        Observe(planner, live, reposLoaded: false);

        Assert.NotNull(Observe(planner, live, reposLoaded: true));
    }

    [Fact]
    public void Only_the_unreachable_notice_is_held_back_before_the_repo_list_loads()
    {
        var toast = Observe(
            new DriftToastPlanner(),
            [Make("save/1", NoticeSeverity.Critical, "A save"), Make("unreachable/g", NoticeSeverity.Warning, "Unknown")],
            reposLoaded: false);

        Assert.Equal("A save", toast!.Title);
    }

    [Fact]
    public void A_long_body_is_cut_at_the_first_sentence_or_on_a_word()
    {
        var sentence = Observe(
            new DriftToastPlanner(),
            [Make("a", NoticeSeverity.Warning, body: "First sentence. Second sentence that is not wanted.")]);

        Assert.Equal("First sentence.", sentence!.Body);

        var longText = string.Join(' ', Enumerable.Repeat("word", 100));
        var cut = Observe(new DriftToastPlanner(), [Make("a", NoticeSeverity.Warning, body: longText)]);

        Assert.True(cut!.Body.Length <= DriftToastPlanner.MaxBodyLength + 3);
        Assert.EndsWith("...", cut.Body);
    }
}
