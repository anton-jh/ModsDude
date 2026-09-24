using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Retention;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One revision, as a row in a profile's history.
/// </summary>
/// <remarks>
/// Everything it shows was recorded when the revision was written. Nothing here diffs two snapshots
/// to render a line: a profile holds one to two thousand mods, and a history page renders tens of
/// these at once.
/// </remarks>
public partial class ProfileRevisionViewModel(ProfileRevisionDto revision, bool isHead) : ObservableObject, ISelectableRow
{
    public ProfileRevisionDto Revision { get; } = revision;

    public int Number { get; } = revision.Number;

    /// <summary>Whether this is the profile's current list - the one a sync would install.</summary>
    public bool IsHead { get; } = isHead;

    /// <summary>
    /// Picked in the history. Any row can be, the head included: a click has to highlight what it
    /// landed on, and what one pick means is showing that revision. Deleting is narrower - see
    /// <see cref="CanPrune"/> - and the page only ever deletes the picked rows that pass it.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>The savegame snapshots played on this revision, each of which keeps it.</summary>
    public IReadOnlyList<SavegameSnapshotRefDto> PlayedOn { get; } = [.. revision.PlayedOn ?? []];

    public bool IsPlayedOn => PlayedOn.Count > 0;

    /// <summary>
    /// Whether pruning would delete this revision. The same two rules the server refuses by, so a
    /// row the page offers is one the server takes - short of a savegame played on it in between,
    /// which the prune's own answer still names.
    /// </summary>
    public bool CanPrune => IsHead is false && IsPlayedOn is false;

    /// <summary>Why pruning keeps this revision, or null where it would not.</summary>
    public string? KeptBecause => IsHead
        ? "The current list cannot be deleted. Edit the profile to change what it pins."
        : IsPlayedOn
            ? $"Cannot be deleted while a savegame snapshot was played on it: {PlayedOnList}. Delete those snapshots first."
            : null;

    public string PlayedOnText => PlayedOn.Count == 1
        ? "Played on"
        : $"Played on ×{PlayedOn.Count}";

    public string PlayedOnTooltip => $"Savegame snapshots played on this revision: {PlayedOnList}.";

    /// <summary>
    /// A glyph for how the revision came about, so a restore or a copy stands out in a column of
    /// ordinary edits without reading every summary.
    /// </summary>
    public string OriginGlyph => Revision.Origin switch
    {
        ProfileRevisionOrigin.Created => "",
        ProfileRevisionOrigin.Copied => "",
        ProfileRevisionOrigin.Restored => "",
        _ => ""
    };

    public string OriginTooltip => Revision.Origin switch
    {
        ProfileRevisionOrigin.Created => "Created",
        ProfileRevisionOrigin.Copied => "Copied",
        ProfileRevisionOrigin.Restored => "Restored",
        _ => "Saved"
    };

    private string PlayedOnList => string.Join(", ", PlayedOn.Select(x => $"{x.SavegameName} · snapshot {x.Number}"));

    public string Title => Revision.Label is { Length: > 0 } label
        ? $"{Number}. {label}"
        : $"Revision {Number}";

    /// <summary>Local time, because a history is read by the person in front of it.</summary>
    public string When => Revision.Created.ToLocalTime().ToString("g");

    public string Author => Revision.CreatedBy.DisplayName;

    /// <summary>
    /// What this revision did, in the words the save itself recorded. A restore says where it came
    /// from rather than reading as an ordinary edit that happens to match an old list.
    /// </summary>
    public string Summary => Revision.Origin switch
    {
        ProfileRevisionOrigin.Created => "Created the profile",
        ProfileRevisionOrigin.Copied => Revision.SourceRevision is int copied
            ? $"Copied from revision {copied} of another profile"
            : "Copied from another profile",
        ProfileRevisionOrigin.Restored => Revision.SourceRevision is int restored
            ? $"Restored revision {restored}"
            : "Restored an earlier revision",
        _ => DescribeChanges()
    };

    public string ModCountText => Revision.ModCount == 1 ? "1 mod" : $"{Revision.ModCount} mods";

    /// <summary>When retention deletes this revision, or null where it is not scheduled.</summary>
    public string? DeletionText => ScheduledDeletion.Describe(Revision.DeletionScheduledFor);

    /// <summary>Why, and what would keep it.</summary>
    public string? DeletionTooltip => ScheduledDeletion.Explain(Revision.DeletionReason, RetainedKind.Revision);

    public bool HasDeletion => DeletionText is not null;


    private string DescribeChanges()
    {
        var parts = new List<string>();

        if (Revision.Changes.Added > 0)
        {
            parts.Add($"{Revision.Changes.Added} added");
        }

        if (Revision.Changes.Changed > 0)
        {
            parts.Add($"{Revision.Changes.Changed} changed");
        }

        if (Revision.Changes.Removed > 0)
        {
            parts.Add($"{Revision.Changes.Removed} removed");
        }

        // A save that changed nothing mints no revision, so this is only reachable for the first
        // revision of a profile that was created empty.
        return parts.Count == 0 ? "No mods" : string.Join(" · ", parts);
    }
}
