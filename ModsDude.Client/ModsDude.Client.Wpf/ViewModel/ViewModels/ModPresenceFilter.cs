namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Which slice of the merged list the Manage page is showing. The three-state "local only / server
/// only / both" is derived here, from the two facts on the version, rather than being stored
/// anywhere. See docs/09-mod-catalog.md#one-identity-two-facts.
/// </summary>
public enum ModPresenceFilter
{
    All,

    /// <summary>Registered in the repo, whether or not the file is on this machine.</summary>
    InRepo,

    /// <summary>Here but not registered - which is exactly what there is to import.</summary>
    OnDiskOnly,

    /// <summary>Registered and pinned by no profile, so a delete would be accepted.</summary>
    Unused
}
