using ModsDude.Client.Core.Profiles.Editor;

namespace ModsDude.Client.Core.Notices;

/// <summary>Where a notice's Review button goes.</summary>
public abstract record NoticeReview
{
    private NoticeReview()
    {
    }


    /// <summary>The profile's mod list, reviewing the game's mod folders with this filter.</summary>
    public sealed record ModList(PinnedModFilter Filter) : NoticeReview;

    /// <summary>The profile's history, with <paramref name="To"/> selected and compared with <paramref name="From"/>.</summary>
    public sealed record RevisionChanges(int From, int To) : NoticeReview;
}
