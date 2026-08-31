using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// What the profile looks like from here: how many mods it pins, which game instances on this
/// machine are set to match it, and whether each of them still does.
/// </summary>
public partial class ProfileOverviewPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly ProfileDto _profile;
    private readonly ProfileService _profileService;
    private readonly InstanceDriftMonitor _driftMonitor;

    private int? _fetchedModCount;


    public ProfileOverviewPageViewModel(
        Repo repo,
        ProfileDto profile,
        ProfileService profileService,
        InstanceDriftMonitor driftMonitor)
    {
        _repo = repo;
        _profile = profile;
        _profileService = profileService;
        _driftMonitor = driftMonitor;

        Instances = [];

        _profileService.ProfileUpdated += OnProfileUpdated;
        _repo.LocalInstances.CollectionChanged += OnInstancesChanged;
        _driftMonitor.Changed += OnDriftChanged;

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
        _driftMonitor.Changed -= OnDriftChanged;
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

    private void OnDriftChanged(object? sender, EventArgs e)
    {
        // The monitor checks off the UI thread, and these rows are bound.
        _ = Application.Current?.Dispatcher.InvokeAsync(RefreshInstances);
    }

    /// <summary>
    /// Only the instances actually set to this profile. An instance the repo offers but that points
    /// somewhere else is the repo overview's business, not this page's.
    /// </summary>
    private void RefreshInstances()
    {
        var drifted = _driftMonitor.Drifted.ToDictionary(x => x.Instance.InstanceId, x => x.Report);

        Instances.Clear();

        var active = new ActiveProfile(_repo.Id, _profile.Id);

        foreach (var instance in _repo.LocalInstances.Where(x => x.ActiveProfile == active))
        {
            Instances.Add(new InstanceOverviewViewModel(
                instance,
                "Set to this profile",
                drifted.GetValueOrDefault(instance.Id)));
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
