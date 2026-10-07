using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Shell.Navigation;
using System.Collections.Specialized;

namespace ModsDude.Client.Wpf.Repos;

/// <summary>
/// The two ways to a repo, side by side: joining one with an invite, and creating one. It is also
/// where the app lands when there is no repo to open, which is why its title changes with the list.
/// </summary>
public sealed class JoinOrCreatePageViewModel : PageViewModel, IDisposable
{
    private readonly IRepoStore _repoStore;


    public JoinOrCreatePageViewModel(
        IRepoStore repoStore,
        JoinRepoFormViewModel join,
        CreateRepoFormViewModel create)
    {
        _repoStore = repoStore;
        Join = join;
        Create = create;

        _repoStore.Repos.CollectionChanged += OnReposChanged;
    }


    public JoinRepoFormViewModel Join { get; }

    public CreateRepoFormViewModel Create { get; }

    public string Title => _repoStore.Repos.Count == 0 ? "Welcome to ModsDude" : "Join or create";


    public void Dispose()
    {
        _repoStore.Repos.CollectionChanged -= OnReposChanged;
        Create.Dispose();
    }


    private void OnReposChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Title));
    }
}
