namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// How a mod on disk relates to what the repo already holds.
/// </summary>
public enum ModStatus
{
    None,
    New,
    UpdateAvailable,
    AlreadyInRepo
}
