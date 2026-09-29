using ModsDude.Client.Core.Profiles.Editor;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>Every count and sentence the profile mod editor shows, from one computed state.</summary>
public sealed class ProfileModsEditorSummary
{
    public ProfileModsEditorSummary(
        ProfileDraft draft,
        ProfileEditorState state,
        bool showIgnored,
        bool hasEnabledFolders,
        string remoteName,
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
        PendingText = $"{Mods(state.PendingCount)} will be imported when you save";

        UpdateCountText = updates.Count switch
        {
            0 when state.RemoteUpdateCount + state.RemoteLockedUpdateCount > 0 => "No updates here",
            0 when hasEnabledFolders => "No updates available",
            0 => "No updates in this repo. No folders are being read.",
            _ when updates.PendingCount > 0
                => $"{Plural(updates.Count, "update")} available · {updates.PendingCount} will be imported when you save",
            _ => $"{Plural(updates.Count, "update")} available"
        };

        HasRemoteUpdates = state.RemoteUpdateCount + state.RemoteLockedUpdateCount > 0;
        RemoteUpdatesText = (state.RemoteUpdateCount, state.RemoteLockedUpdateCount) switch
        {
            (0, var locked) => $"{locked} locked on {remoteName}",
            (var free, 0) => updates.Count > 0 ? $"{free} more on {remoteName}" : $"{free} on {remoteName}",
            var (free, locked) => updates.Count > 0
                ? $"{free} more on {remoteName} · {locked} locked"
                : $"{free} on {remoteName} · {locked} locked"
        };

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
    public bool HasRemoteUpdates { get; }
    public string RemoteUpdatesText { get; }
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

    /// <summary>"412 mods" when nothing is hidden, "42 of 412 mods" when something is.</summary>
    private static string Describe(int shown, int total) => shown == total ? Mods(total) : $"{shown} of {Mods(total)}";
}
