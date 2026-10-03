namespace ModsDude.Client.Core.Models;

/// <summary>
/// A publish sent from a slot and not yet answered. Publishing the same bytes from the same slot
/// again repeats it under the same ids, so a lost answer does not end in "name taken".
/// </summary>
/// <param name="ContentHash">The packed bytes the request was for. Other bytes are another publish.</param>
/// <param name="SavegameId">The id the bytes were uploaded under and the savegame is created with.</param>
public sealed record SavegamePendingPublish(
    SavegameSlotRef Slot,
    string ContentHash,
    Guid RequestId,
    Guid SavegameId);
