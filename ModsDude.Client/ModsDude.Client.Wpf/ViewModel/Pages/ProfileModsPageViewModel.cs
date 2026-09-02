using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// What the profile pins, for somebody who cannot change it.
/// </summary>
/// <remarks>
/// The same sidebar entry as <see cref="ProfileModsEditorPageViewModel"/>, chosen by membership
/// level in <see cref="ProfilePageViewModel"/>. A guest can sync this profile into their game, so
/// what it contains is a fair question for them to ask - and the editor is the wrong answer, being
/// a two-list drag surface whose every control would have to be switched off.
/// </remarks>
public partial class ProfileModsPageViewModel : PageViewModel
{
    private readonly Repo _repo;
    private readonly ProfileDto _profile;
    private readonly ProfileService _profileService;
    private readonly ModListItemViewModel.Factory _itemFactory;

    private IReadOnlyList<PinnedMod> _fetched = [];


    public ProfileModsPageViewModel(
        Repo repo,
        ProfileDto profile,
        ProfileService profileService,
        ModListItemViewModel.Factory itemFactory)
    {
        _repo = repo;
        _profile = profile;
        _profileService = profileService;
        _itemFactory = itemFactory;

        Mods = [];
    }


    public string ProfileName => _profile.Name;
    public string RepoName => _repo.Name;

    public ObservableCollection<PinnedModViewModel> Mods { get; }

    /// <summary>
    /// Held until the fetch lands, so an empty list reads as "nothing pinned" only once that is
    /// actually known rather than for the moment before the answer arrives.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading = true;

    public bool HasMods => Mods.Count > 0;
    public bool HasNoMods => Mods.Count == 0;


    protected override async Task InitAsync()
    {
        _fetched = await _profileService.GetPinnedMods(_repo.Id, _profile.Id, CancellationToken.None);
    }

    protected override void OnInitCompleted()
    {
        foreach (var mod in _fetched)
        {
            Mods.Add(new PinnedModViewModel(mod, _repo.Id, _itemFactory));
        }

        IsLoading = false;

        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(HasNoMods));
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public ProfileModsPageViewModel Create(Repo repo, ProfileDto profile)
            => ActivatorUtilities.CreateInstance<ProfileModsPageViewModel>(serviceProvider, repo, profile);
    }
}
