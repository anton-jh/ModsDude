using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// What the profile looks like from here: how many mods it pins, and which game instances on this
/// machine are set to match it. Drift against those instances and the time each last synced belong
/// here as well - see docs/PLAN.md#phase-4--make-drift-unmissable - but neither has a source yet.
/// </summary>
public partial class ProfileOverviewPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly ProfileDto _profile;
    private readonly ProfileService _profileService;

    private int? _fetchedModCount;


    public ProfileOverviewPageViewModel(
        Repo repo,
        ProfileDto profile,
        ProfileService profileService)
    {
        _repo = repo;
        _profile = profile;
        _profileService = profileService;

        Instances = [];

        _profileService.ProfileUpdated += OnProfileUpdated;
        _repo.LocalInstances.CollectionChanged += OnInstancesChanged;

        RefreshInstances();
    }


    public string ProfileName => _profile.Name;
    public string RepoName => _repo.Name;
    public ObservableCollection<InstanceOverviewViewModel> Instances { get; }

    public bool HasInstances => Instances.Count > 0;
    public bool HasNoInstances => Instances.Count == 0;

    [ObservableProperty]
    private string _modSummary = "Counting mods...";


    public void Dispose()
    {
        _profileService.ProfileUpdated -= OnProfileUpdated;
        _repo.LocalInstances.CollectionChanged -= OnInstancesChanged;
    }


    protected override async Task InitAsync()
    {
        _fetchedModCount = await _profileService.GetModCount(_repo.Id, _profile.Id, CancellationToken.None);
    }

    protected override void OnInitCompleted()
    {
        ModSummary = _fetchedModCount switch
        {
            0 or null => "No mods pinned yet.",
            1 => "1 mod pinned.",
            var count => $"{count} mods pinned."
        };
    }


    private void OnProfileUpdated(Guid profileId)
    {
        if (profileId == _profile.Id)
        {
            OnPropertyChanged(nameof(ProfileName));
        }
    }

    private void OnInstancesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshInstances();
    }

    /// <summary>
    /// Only the instances actually set to this profile. An instance the repo offers but that points
    /// somewhere else is the repo overview's business, not this page's.
    /// </summary>
    private void RefreshInstances()
    {
        Instances.Clear();

        var active = new ActiveProfile(_repo.Id, _profile.Id);

        foreach (var instance in _repo.LocalInstances.Where(x => x.ActiveProfile == active))
        {
            Instances.Add(new InstanceOverviewViewModel(instance, "Set to this profile"));
        }

        OnPropertyChanged(nameof(HasInstances));
        OnPropertyChanged(nameof(HasNoInstances));
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public ProfileOverviewPageViewModel Create(Repo repo, ProfileDto profile)
            => ActivatorUtilities.CreateInstance<ProfileOverviewPageViewModel>(serviceProvider, repo, profile);
    }
}
