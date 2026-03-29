using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.ViewModelFactories;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

public class InstanceItemViewModel(
    RepoModel repo,
    LocalInstance instance,
    EditLocalInstancePageViewModelFactory pageFactory)
    : MenuItemViewModel(
        instance.Name,
        () => pageFactory.Create(repo, instance),
        instance,
        () => instance.Name,
        nameof(LocalInstance.Name));
