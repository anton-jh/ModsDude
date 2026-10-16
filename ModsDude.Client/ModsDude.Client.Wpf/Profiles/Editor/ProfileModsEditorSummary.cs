using ModsDude.Client.Core.Profiles.Editor;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Wpf.Profiles.Editor;

/// <summary>Every count and sentence the profile mod editor shows, from one computed state.</summary>
public sealed class ProfileModsEditorSummary
{
    public ProfileModsEditorSummary(
        ProfileDraft draft,
        ProfileEditorState state,
        bool showIgnored,
        bool hasApplyTarget)
    {
        var updates = state.Updates;
        var changes = draft.Changes;
        var shownPins = state.PinnedShown.Count(x => x.IsTakenOut is false);

        AvailableCountText = Describe(state.AvailableShown.Count, state.AvailableTotal);

        HasIgnored = state.IgnoredCount > 0;
        IgnoredText = $"{state.IgnoredCount} ignored";
        IgnoredToggleTooltip = showIgnored
            ? "Showing ignored mods, dimmed. Click to hide them again."
            : state.IgnoredCount == 0
                ? "Nothing is ignored here. Mods you ignore are hidden from this list."
                : "Mods you have ignored in this profile are hidden. Click to show them.";

        PinnedCountText = Describe(shownPins, state.ResultCount);
        PinnedEmptyText = state.Pinned.Count == 0
            ? "Nothing in this profile yet.\nAdd mods from the list on the left."
            : state.PinnedShown.Count == 0 ? "Nothing in this profile matches the search and the filter." : null;

        HasModListChanges = changes.IsEmpty is false;
        HasUnsavedChanges = draft.HasChanges;
        ChangeSummary = string.Join(" · ", new[]
        {
            changes.Added.Count > 0 ? $"{changes.Added.Count} added" : null,
            changes.Changed.Count > 0 ? $"{changes.Changed.Count} changed" : null,
            changes.Removed.Count > 0 ? $"{changes.Removed.Count} taken out" : null
        }.OfType<string>());

        HasPending = state.PendingCount > 0;
        PendingText = state.DownloadCount > 0
            ? $"{Mods(state.PendingCount)} will be imported when you save · {state.DownloadCount} downloaded first"
            : $"{Mods(state.PendingCount)} will be imported when you save";

        UpdateCountText = updates.Count == 0
            ? "No updates"
            : string.Join(" · ", new[]
            {
                $"{Plural(updates.Count, "update")} available",
                updates.PendingCount > 0 ? $"{updates.PendingCount} imported on save" : null,
                updates.DownloadCount > 0 ? DescribeDownloads(updates) : null
            }.OfType<string>());

        ApplyUpdatesText = updates.Available.Count == 0 ? "Update all" : $"Update {Mods(updates.Available.Count)}";
        HasRepoOnlyUpdates = updates.FreeCount > 0 && updates.FreeCount < updates.Available.Count;
        UpdateRepoOnlyText = $"Update the {updates.FreeCount} already in the repo";

        HasSkippedUpdates = updates.Skipped.Count > 0;
        SkippedText = $"{updates.Skipped.Count} locked, skipped";

        HasUnsettled = state.UnsettledCount > 0;
        UnsettledText = $"{Plural(state.UnsettledCount, "version")} could not be compared";

        WillApply = hasApplyTarget && HasModListChanges;
        SaveActionText = ProfileApplyTarget.DescribeSaveAction(WillApply);
    }


    public string AvailableCountText { get; }

    public bool HasIgnored { get; }
    public string IgnoredText { get; }
    public string IgnoredToggleTooltip { get; }

    public string PinnedCountText { get; }

    /// <summary>Why the right list is empty, or null where it is not.</summary>
    public string? PinnedEmptyText { get; }

    public bool HasModListChanges { get; }
    public bool HasUnsavedChanges { get; }
    public string ChangeSummary { get; }

    public bool HasPending { get; }
    public string PendingText { get; }

    public string UpdateCountText { get; }
    public string ApplyUpdatesText { get; }
    public bool HasRepoOnlyUpdates { get; }
    public string UpdateRepoOnlyText { get; }
    public bool HasSkippedUpdates { get; }
    public string SkippedText { get; }
    public bool HasUnsettled { get; }
    public string UnsettledText { get; }

    public bool WillApply { get; }
    public string SaveActionText { get; }


    public static string Mods(int count) => Plural(count, "mod");

    public static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static string DescribeDownloads(Core.Profiles.ProfileModUpdatePlan updates)
        => updates.DownloadSources is [var source]
            ? $"{updates.DownloadCount} downloaded from {source}"
            : $"{updates.DownloadCount} downloaded";

    /// <summary>"412 mods" when nothing is hidden, "42 of 412 mods" when something is.</summary>
    private static string Describe(int shown, int total) => shown == total ? Mods(total) : $"{shown} of {Mods(total)}";
}
