using CommunityToolkit.Mvvm.ComponentModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

public partial class ModalViewModel : ObservableObject
{

    public delegate void ModalDoneHandler();
    public event ModalDoneHandler? Completed;


    [ObservableProperty]
    private bool _done;


    partial void OnDoneChanged(bool value)
    {
        if (value == true)
        {
            Completed?.Invoke();
        }
    }
}
