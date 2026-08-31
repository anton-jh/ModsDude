using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// What the repo looks like from here: the game instances it offers, whether each still matches its
/// profile, the profiles it holds, and the caller's standing in it.
/// </summary>
public partial class RepoOverviewPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly ProfileService _profileService;
    private readonly MembershipService _membershipService;
    private readonly InstanceDriftMonitor _driftMonitor;

    private int? _fetchedMemberCount;


    public RepoOverviewPageViewModel(
        Repo repo,
        ProfileService profileService,
        MembershipService membershipService,
        InstanceDriftMonitor driftMonitor)
    {
        _repo = repo;
        _profileService = profileService;
        _membershipService = membershipService;
        _driftMonitor = driftMonitor;

        Instances = [];

        _repo.PropertyChanged += OnRepoPropertyChanged;
        _repo.LocalInstances.CollectionChanged += OnSourceCollectionChanged;
        _profileService.Profiles.CollectionChanged += OnSourceCollectionChanged;
        _driftMonitor.Changed += OnDriftChanged;

        RefreshInstances();
    }


    public string RepoName => _repo.Name;
    public string Game => _repo.Adapter.DisplayName;
    public ObservableCollection<InstanceOverviewViewModel> Instances { get; }

    public string MembershipSummary => _repo.MembershipLevel switch
    {
        RepoMembershipLevel.Admin => "You are an admin of this repo.",
        RepoMembershipLevel.Member => "You are a member of this repo.",
        _ => "You are a guest in this repo."
    };

    public string ProfileSummary => _profileService.Profiles.Count switch
    {
        0 => "No profiles yet.",
        1 => "1 profile.",
        var count => $"{count} profiles."
    };

    public bool HasInstances => Instances.Count > 0;
    public bool HasNoInstances => Instances.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMemberSummary))]
    private string? _memberSummary;

    public bool HasMemberSummary => MemberSummary is not null;


    public void Dispose()
    {
        _repo.PropertyChanged -= OnRepoPropertyChanged;
        _repo.LocalInstances.CollectionChanged -= OnSourceCollectionChanged;
        _profileService.Profiles.CollectionChanged -= OnSourceCollectionChanged;
        _driftMonitor.Changed -= OnDriftChanged;
    }


    protected override async Task InitAsync()
    {
        // Reading the member list needs Member, so for a guest there is simply nothing to say.
        if (_repo.MembershipLevel < RepoMembershipLevel.Member)
        {
            return;
        }

        _fetchedMemberCount = (await _membershipService.GetMembers(_repo.Id, CancellationToken.None)).Count;
    }

    protected override void OnInitCompleted()
    {
        MemberSummary = _fetchedMemberCount switch
        {
            null => null,
            1 => "You are its only member.",
            var count => $"{count} members."
        };
    }


    private void OnRepoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(RepoName));
        OnPropertyChanged(nameof(Game));
        OnPropertyChanged(nameof(MembershipSummary));
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshInstances();

        OnPropertyChanged(nameof(ProfileSummary));
    }

    private void OnDriftChanged(object? sender, EventArgs e)
    {
        // The monitor checks off the UI thread, and these rows are bound.
        _ = Application.Current?.Dispatcher.InvokeAsync(RefreshInstances);
    }

    private void RefreshInstances()
    {
        var drifted = _driftMonitor.Drifted.ToDictionary(x => x.Instance.InstanceId, x => x.Report);

        Instances.Clear();

        foreach (var instance in _repo.LocalInstances)
        {
            Instances.Add(new InstanceOverviewViewModel(
                instance,
                DescribeActiveProfile(instance),
                drifted.GetValueOrDefault(instance.Id)));
        }

        OnPropertyChanged(nameof(HasInstances));
        OnPropertyChanged(nameof(HasNoInstances));
    }

    private string DescribeActiveProfile(LocalInstance instance)
    {
        if (instance.ActiveProfile is not ActiveProfile active)
        {
            return "No profile set";
        }

        // An instance is offered by every repo targeting the same game, so the one it is currently
        // set to may well belong to a different repo than the one being looked at.
        if (active.RepoId != _repo.Id)
        {
            return "Set to a profile in another repo";
        }

        return _profileService.Profiles.FirstOrDefault(x => x.Id == active.ProfileId) is ProfileDto profile
            ? $"Set to '{profile.Name}'"
            : "Set to a profile that no longer exists";
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoOverviewPageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<RepoOverviewPageViewModel>(serviceProvider, repo);
    }
}
