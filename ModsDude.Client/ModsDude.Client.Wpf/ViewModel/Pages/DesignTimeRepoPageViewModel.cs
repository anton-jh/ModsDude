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
                AdapterId = "test_placeholder",
                AdapterConfiguration = ""
            },
            null!,
            null!,
            null!,
            null!,
            null!)
    {
    }
}
