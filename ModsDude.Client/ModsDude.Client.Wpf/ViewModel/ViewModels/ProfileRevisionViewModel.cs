using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One revision, as a row in a profile's history.
/// </summary>
/// <remarks>
/// Everything it shows was recorded when the revision was written. Nothing here diffs two snapshots
/// to render a line: a profile holds one to two thousand mods, and a history page renders tens of
/// these at once.
/// </remarks>
public partial class ProfileRevisionViewModel(ProfileRevisionDto revision, bool isHead) : ObservableObject
{
    public ProfileRevisionDto Revision { get; } = revision;

    public int Number { get; } = revision.Number;

    /// <summary>Whether this is the profile's current list - the one a sync would install.</summary>
    public bool IsHead { get; } = isHead;

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
