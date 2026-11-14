using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.Profiles.Editor;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Sidebar;

namespace ModsDude.Client.Wpf.Profiles;

/// <summary>
/// Follows the profile's name rather than snapshotting it, the way the repo entries follow theirs, and
/// is marked while its mod list editor is kept hidden with unsaved changes.
/// </summary>
public class ProfileItemViewModel
    : MenuItemViewModel
{
    private readonly Profile _profile;
    private readonly IRetainedPages _retainedPages;
    private readonly ProfileModsEditorKey _modsEditorKey;


    public ProfileItemViewModel(
        Repo repo,
        Profile profile,
        ProfilePageViewModel.Factory profilePageViewModelFactory,
        IRetainedPages retainedPages)
        : base(
            profile.Name,
            () => profilePageViewModelFactory.Create(repo, profile),
            profile,
            () => profile.Name,
            nameof(Profile.Name))
    {
        _profile = profile;
        _retainedPages = retainedPages;
        _modsEditorKey = new ProfileModsEditorKey(repo.Id, profile.Id);

        Icon = MenuIcons.Profile;

        _retainedPages.Changed += OnRetainedPagesChanged;
        OnRetainedPagesChanged();
    }


    public Guid Id => _profile.Id;

    public override bool IsEntity => true;


    public override void Dispose()
    {
        _retainedPages.Changed -= OnRetainedPagesChanged;

        base.Dispose();
    }

    private void OnRetainedPagesChanged()
        => Attention = _retainedPages.IsKept(_modsEditorKey) ? "Unsaved changes" : null;
}
