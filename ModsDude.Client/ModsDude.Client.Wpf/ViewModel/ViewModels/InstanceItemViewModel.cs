using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

public class InstanceItemViewModel(
    Repo repo,
    LocalInstance instance,
    InstancePageViewModel.Factory pageFactory)
    : MenuItemViewModel(
        instance.Name,
        () => pageFactory.Create(repo, instance),
        instance,
        () => instance.Name,
        nameof(LocalInstance.Name));
