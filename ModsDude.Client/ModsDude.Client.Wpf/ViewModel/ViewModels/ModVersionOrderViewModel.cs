using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One mod's versions, oldest first, with the ones the user is allowed to move. Shared by the
/// arbitration dialog and the manual reorder, because they are the same operation - deciding where a
/// version sits among its siblings - reached from two directions.
/// </summary>
/// <remarks>
/// Movement is a swap with the adjacent entry rather than a free drop, and only a movable entry
/// initiates it. That is what keeps arbitration's rule - versions the repo already holds may not be
/// reordered relative to each other - true by construction: a pinned entry only ever shifts because
/// a movable one passed it, which leaves the pinned ones in the order they arrived in.
/// </remarks>
public partial class ModVersionOrderViewModel : ObservableObject
{
    public ModVersionOrderViewModel(IEnumerable<ModVersionOrderEntry> entries)
    {
        Entries = [.. entries.Select(x => new ModVersionOrderEntryViewModel(x))];

        UpdateMovability();
    }


    public ObservableCollection<ModVersionOrderEntryViewModel> Entries { get; }

    /// <summary>The order as it currently stands, oldest first.</summary>
    public IReadOnlyList<ModVersionKey> Order => [.. Entries.Select(x => x.VersionId)];

    /// <summary>
    /// True while something the ordering could not place is still where the derivation left it. Not
    /// an error - the derived position is often right - but it is the thing the dialog is asking
    /// about, so it is worth being able to say whether anything was actually looked at.
    /// </summary>
    public bool HasUnplaceableEntries => Entries.Any(x => x.IsUnplaceable);


    [RelayCommand]
    private void MoveUp(ModVersionOrderEntryViewModel? entry)
    {
        Move(entry, -1);
    }

    [RelayCommand]
    private void MoveDown(ModVersionOrderEntryViewModel? entry)
    {
        Move(entry, 1);
    }


    private void Move(ModVersionOrderEntryViewModel? entry, int offset)
    {
        if (entry is null || entry.IsMovable is false)
        {
            return;
        }

        var from = Entries.IndexOf(entry);
        var to = from + offset;

        if (from < 0 || to < 0 || to >= Entries.Count)
        {
            return;
        }

        Entries.Move(from, to);

        UpdateMovability();
    }

    private void UpdateMovability()
    {
        for (var index = 0; index < Entries.Count; index++)
        {
            var entry = Entries[index];

            entry.CanMoveUp = entry.IsMovable && index > 0;
            entry.CanMoveDown = entry.IsMovable && index < Entries.Count - 1;
            entry.Position = index + 1;
        }
    }
}


/// <param name="IsMovable">
/// False for a version the repo already holds while an import is being placed around it: a placement
/// can only insert, so nothing here can move one registered version past another.
/// </param>
/// <param name="IsUnplaceable">
/// Whether this is one of the versions nothing could order, which is what made the mod a question in
/// the first place. Drawn differently so the user can see what they are actually being asked about.
/// </param>
/// <param name="Note">
/// Where the version came from, where that distinction is part of the question. Null for a list
/// whose entries are all the same kind, such as a manual reorder of what the repo already holds.
/// </param>
public readonly record struct ModVersionOrderEntry(
    ModVersionKey VersionId,
    bool IsMovable,
    bool IsUnplaceable,
    string? Note = null);


public partial class ModVersionOrderEntryViewModel(ModVersionOrderEntry entry) : ObservableObject
{
    public ModVersionKey VersionId { get; } = entry.VersionId;

    public string Version => VersionId.Value;

    public bool IsMovable { get; } = entry.IsMovable;

    public bool IsUnplaceable { get; } = entry.IsUnplaceable;

    public string? Note { get; } = entry.Note;

    public bool HasNote => string.IsNullOrWhiteSpace(Note) is false;

    [ObservableProperty]
    private int _position;

    [ObservableProperty]
    private bool _canMoveUp;

    [ObservableProperty]
    private bool _canMoveDown;
}
