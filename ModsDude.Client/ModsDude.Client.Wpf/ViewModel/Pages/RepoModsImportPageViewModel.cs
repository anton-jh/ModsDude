using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class RepoModsImportPageViewModel(Repo repo)
    : PageViewModel
{
    public ObservableCollection<LocalMod> LocalMods { get; private set; } = [];

    public string RepoName { get; } = repo.Name;


    [RelayCommand]
    public async Task ImportCommand()
    {

    }

    public override async void Init() // TODO make Init an async Task and handle loading in the background
    {
        var mods = new List<LocalMod>();

        foreach (var instance in repo.LocalInstances)
        {
            var installedMods = await instance.Adapter.GetInstanceCapabilityAdapterFactory<IInstanceModAdapter>().GetInstalledMods(default);
            mods.AddRange(installedMods);
        }

        LocalMods = new(mods);
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoModsImportPageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<RepoModsImportPageViewModel>(serviceProvider, repo);
    }
}
