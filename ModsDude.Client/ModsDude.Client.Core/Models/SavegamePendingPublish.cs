namespace ModsDude.Client.Core.Models;

/// <summary>
/// A publish sent from a slot and not yet answered. Publishing the same contents from the same slot
/// again repeats it under the same ids, so a lost answer does not end in "name taken"; publishing
/// anything else from that slot first finds out whether this one landed.
/// </summary>
/// <param name="ContentHash">What the request uploaded. Other contents are another publish.</param>
/// <param name="SlotHash">What the slot hashed to when it was packed for this request.</param>
/// <param name="SavegameId">The id the bytes were uploaded under and the savegame is created with.</param>
public sealed record SavegamePendingPublish(
    SavegameSlotRef Slot,
    Guid RepoId,
    string ContentHash,
    string SlotHash,
    Guid RequestId,
    Guid SavegameId);
