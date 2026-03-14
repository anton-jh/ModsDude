using CommunityToolkit.Mvvm.ComponentModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public abstract class PageViewModel : ObservableObject
{
    public virtual void Init() { }
}
