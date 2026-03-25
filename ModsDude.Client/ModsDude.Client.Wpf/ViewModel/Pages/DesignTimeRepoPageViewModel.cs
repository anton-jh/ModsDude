using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public class DesignTimeRepoPageViewModel
    : RepoPageViewModel
{
    public DesignTimeRepoPageViewModel()
        : base(
            new RepoModel()
            {
                Id = default,
                Name = "Test repo 123",
                AdapterId = new GameAdapterId("_example", 1),
                AdapterConfiguration = null!
            },
            null!,
            null!,
            null!,
            null!,
            null!,
            null!)
    {
    }
}
