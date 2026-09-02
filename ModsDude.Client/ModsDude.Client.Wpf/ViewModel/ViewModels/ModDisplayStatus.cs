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
public enum ModDisplayStatus
{
    None,
    New,
    UpdateAvailable,
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
