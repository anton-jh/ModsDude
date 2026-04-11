using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class RepoModsImportPageViewModel(
    RepoModel repo,
    IGameAdapterIndex gameAdapterIndex,
    LocalInstanceService localInstanceService)
    : PageViewModel
{
    public ObservableCollection<LocalMod> LocalMods { get; private set; } = [];

    public string RepoName { get; } = repo.Name;


    [RelayCommand]
    public async Task ImportCommand()
    {

    }

    public override async void Init()
    {
        var adapter = gameAdapterIndex.GetById(repo.AdapterId);
        var modAdapter = adapter.ModAdapter
            ?? throw MissingModSupportExceptionThrowHelper();

        var mods = new List<LocalMod>();

        foreach (var instance in localInstanceService.GetByRepoId(repo.Id))
        {
            var instanceSettings = adapter.DeserializeInstanceSettings(instance.AdapterInstanceSettings);
            var installedMods = await modAdapter.GetModsFromInstalled(instanceSettings);
            mods.AddRange(installedMods);
        }

        LocalMods = new(mods);
    }


    private UserFriendlyException MissingModSupportExceptionThrowHelper()
    {
        return new UserFriendlyException(
            "This game adapter doesn't support mods",
            $"Game adapter '{repo.AdapterId}' does not support mods");
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoModsImportPageViewModel Create(RepoModel repo)
            => ActivatorUtilities.CreateInstance<RepoModsImportPageViewModel>(serviceProvider, repo);
    }
}
