using ModsDude.Client.Wpf.Shell.Navigation;

namespace ModsDude.Client.Wpf.Profiles;
public class ExamplePageViewModel(string title, string subtitle = "Example page")
    : PageViewModel
{
    public ExamplePageViewModel()
        : this("Test 123")
    {
    }


    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
}
