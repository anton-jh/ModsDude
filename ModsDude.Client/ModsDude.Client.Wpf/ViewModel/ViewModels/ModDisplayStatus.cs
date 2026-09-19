using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// What a row says about a mod version, for one page's purposes.
/// </summary>
/// <remarks>
/// Judgment, not fact. The facts - <see cref="CatalogModVersion.IsLocal"/> and
/// <see cref="CatalogModVersion.IsOnServer"/> - live on the version and are never stored as a
/// three-state, because storing the derived value means two sources of truth for one question.
/// "New" means different things on the import list and in the profile editor, which is why the
/// derivation belongs to the page that has a context rather than to the model.
/// </remarks>
/// <remarks>
/// <para>
/// <b>Four states, two colours, three words</b> in the profile editor, where the left list is now
/// about versions rather than mods and a row can be an update to something the profile already
/// holds. A chip's fill says what the version means for the <em>repo</em> and its text says what it
/// means for <em>this profile</em>: accent where the move is free because the repo holds it, green
/// where saving would have to upload the file first.
/// </para>
/// </remarks>
public enum ModDisplayStatus
{
    None,

    /// <summary>Not in the repo. Saving imports it.</summary>
    New,

    /// <summary>
    /// A newer version of a mod this profile pins, and the repo holds it - so the move costs
    /// nothing. The accent, because it is the one of the four that is free.
    /// </summary>
    UpdateAvailable,

    /// <summary>
    /// The same, for a version that is only on disk: an update to a pinned mod that saving imports.
    /// Green, because it is an import like the other two green ones.
    /// </summary>
    UpdatePending,

    /// <summary>
    /// Newer than anything the repo holds, of a mod this profile does <em>not</em> pin. An import
    /// candidate - which is what the repo mods page calls an <em>Update</em> from its own point of
    /// view, and which is not one from here: nothing in this profile moves by taking it.
    /// </summary>
    NewVersion,

    /// <summary>
    /// An older version of a mod this profile pins, which pressing the row's button would move the pin
    /// back to. Said as its own word because the button is the same one an update uses, and a row that
    /// looks like every other row over a move that goes backwards is one people press by accident - a
    /// downgrade is the one move here that can leave a savegame ahead of its mods.
    /// </summary>
    Downgrade,

    AlreadyInRepo,

    /// <summary>
    /// A draft has taken this mod out of the list it belongs to, and the removal is not written yet.
    /// The counterpart of the pending-import chip on the other side: a row that has moved but has
    /// not been saved looks exactly like one that was always there, and the chip is what says
    /// otherwise.
    /// </summary>
    PendingRemoval
}

public static class ModDisplayStatusExtensions
{
    /// <summary>
    /// How the import and management lists read a version: registered or not. No version-string
    /// parsing is involved - a local version either has a server counterpart or it does not.
    /// </summary>
    public static ModDisplayStatus GetImportStatus(this CatalogModVersion version)
    {
        return version.IsOnServer ? ModDisplayStatus.AlreadyInRepo : ModDisplayStatus.New;
    }
}
