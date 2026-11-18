using ModsDude.Client.Core.Profiles.Editor;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Notices;

/// <summary>Where a notice's Review button goes.</summary>
public abstract record NoticeReview
{
    private NoticeReview()
    {
    }


    /// <summary>The profile's mod list, reviewing the game's mod folders with this filter.</summary>
    public sealed record ModList(PinnedModFilter Filter) : NoticeReview;

    /// <summary>The profile's history, with the gap's target revision selected and compared with its applied one.</summary>
    public sealed record RevisionChanges(RevisionGap Gap) : NoticeReview;
}
