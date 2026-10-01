using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;
using System.Text;

namespace ModsDude.Client.Wpf.Savegames;

/// <summary>
/// How a slot is described, shared by every savegame modal and toast so they describe it one way.
/// </summary>
internal static class SavegameSlotWording
{
    /// <summary>
    /// How many of the adapter's details fit on a row's one line. Its order is its priority order,
    /// which is what makes taking a prefix of it a reasonable thing to do.
    /// </summary>
    public const int DetailsOnTheRow = 3;


    /// <summary>
    /// How a slot is named in a sentence: by its number where the game has them, and always by the save it
    /// holds. <c>slot 4 ('Zielonka')</c>, or just <c>'Zielonka'</c> for a game whose slots are not numbered.
    /// </summary>
    public static string Named(int? number, string label)
        => number is int slot ? $"slot {slot} ('{label}')" : $"'{label}'";

    public static string Capitalised(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>
    /// What a hand-back says where the claim went back but the slot would not go to the Recycle Bin.
    /// Suggesting deletion is safe: nothing is handed back until the bytes are on the server.
    /// </summary>
    public static string LeftBehind(string slot)
        => $"Your copy could not be moved to the Recycle Bin, so it is still in {slot} - an ordinary save ModsDude " +
           "no longer tracks. Delete it yourself, or keep it as a save of your own.";

    /// <summary>What a hand-back says where the slot was not recycled, or null where it was.</summary>
    public static string? NotRecycled(SavegameLocalCopy localCopy, string slot) => localCopy switch
    {
        SavegameLocalCopy.LeftBehind => LeftBehind(slot),

        SavegameLocalCopy.ChangedSinceUpload =>
            $"{Capitalised(slot)} changed after it was uploaded - the game may have saved again - so it was left where it is " +
            "rather than recycled. That change is not in the snapshot; the copy is now an ordinary save ModsDude no longer tracks.",

        _ => null
    };

    /// <summary>
    /// Everything about a slot, for the tooltip: what the save is called, every detail the adapter
    /// recorded, and - last - where on this machine it is.
    /// </summary>
    /// <param name="targetName">
    /// What the game calls the folder this slot is in, from <see cref="TargetNames.Distinguishing"/>:
    /// null for a game with one, which is nearly every game.
    /// </param>
    /// <remarks>
    /// <b>The last line is the slot's number, where the game has them, and then the adapter's slot id and,
    /// where there is something to tell apart, the folder's name.</b> Never the target <em>key</em>: that is an adapter-authored identity that
    /// ends up in filenames, it is not chosen to be read, and "game:savegame1" in front of somebody
    /// choosing where a save goes is a worse answer than "savegame1" - which is what the game itself
    /// calls that folder, and is the one thing here they can go and look at.
    /// </remarks>
    public static string DescribeFully(
        string label, SavegameSlotRef id, string? targetName, IReadOnlyList<SavegameDetail> details, int? number = null)
    {
        var text = new StringBuilder(label);

        foreach (var detail in details)
        {
            text.Append('\n').Append(detail.Label).Append(": ").Append(detail.Value);
        }

        var where = targetName is { Length: > 0 } folder
            ? $"{folder} · {id.Slot.Value}"
            : id.Slot.Value;

        return text.Append('\n').Append(where).ToString();
    }
}
