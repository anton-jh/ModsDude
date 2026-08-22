using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class RepoModsImportPageViewModel(Repo repo)
    : PageViewModel
{
    private readonly IBaseModAdapter _baseModAdapter = repo.Adapter.GetBaseCapabilityAdapterFactory<IBaseModAdapter>()?.Invoke()
        ?? throw UserFriendlyException.RepoNoModSupport();


    [ObservableProperty]
    private ObservableCollection<LocalMod> _localMods = [];

    public string RepoName { get; } = repo.Name;


    [RelayCommand]
    public async Task ImportAsync()
    {
        
    }

    protected override void Init()
    {
        
    }

    protected override async Task InitAsync()
    {
        await Task.Yield();

        var mods = new List<LocalMod>();

        foreach (var instance in repo.LocalInstances)
        {
            var installedMods = await _baseModAdapter.WithInstanceSettings(instance.InstanceSettings).GetInstalledMods(default);
            mods.AddRange(installedMods);
        }

        LocalMods = new(mods.DistinctBy(x => (x.Id, x.Version)));
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoModsImportPageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<RepoModsImportPageViewModel>(serviceProvider, repo);
    }
}
