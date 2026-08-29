using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One game instance as an overview shows it: where it installs and which profile it is meant to
/// match. Read-only and rebuilt whenever the underlying lists change - the instance's own page is
/// where it is edited.
/// </summary>
public class InstanceOverviewViewModel(
    LocalInstance instance,
    string activeProfileSummary)
{
    public string Name { get; } = instance.Name;

    public string ModFolder { get; } = instance.ModFolder ?? "No mod folder configured";

    public string ActiveProfileSummary { get; } = activeProfileSummary;
}
