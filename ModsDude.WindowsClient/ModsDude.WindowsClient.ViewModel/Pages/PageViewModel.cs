using CommunityToolkit.Mvvm.ComponentModel;

namespace ModsDude.WindowsClient.ViewModel.Pages;
public abstract class PageViewModel : ObservableObject
{
    public virtual void Init() { }
}
