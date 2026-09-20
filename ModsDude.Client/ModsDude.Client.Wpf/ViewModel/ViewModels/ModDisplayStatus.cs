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
/// A status is a fact about a version, in the profile editor where the left list is about versions
/// rather than mods and a row can be an update to something the profile already holds. Its text says
/// what the version means for <em>this profile</em>, and it is drawn as an outline there: a filled chip
/// is reserved for what the draft has done to a mod, which is a <see cref="ProfileModTouch"/> and not
/// a status.
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

    AlreadyInRepo,

    /// <summary>
    /// A version the draft pins and the repo does not hold, on the review of what a save will do: it is
    /// uploaded and registered first. A fact about the version, so it is an outline like the rest.
    /// </summary>
    ImportsOnSave
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
