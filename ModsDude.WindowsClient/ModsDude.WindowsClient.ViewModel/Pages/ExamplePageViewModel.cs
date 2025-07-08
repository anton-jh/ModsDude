namespace ModsDude.WindowsClient.ViewModel.Pages;
public class ExamplePageViewModel(string text)
    : PageViewModel
{
    public ExamplePageViewModel()
        : this("Test 123")
    {
    }


    public string Text { get; } = text;
}
