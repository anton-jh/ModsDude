using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

public partial class ConfirmationDialogViewModel(
    string title,
    string message,
    IconKind icon,
    string yesText = "Yes",
    string noText = "No")
    : ModalViewModel
{
    public string Title { get; } = title;
    public string Message { get; } = message;
    public IconKind Icon { get; } = icon;
    public string YesText { get; } = yesText;
    public string NoText { get; } = noText;

    public bool Result { get; private set; }


    [RelayCommand]
    public void SetYes()
    {
        Result = true;
        Done = true;
    }

    [RelayCommand]
    public void SetNo()
    {
        Result = false;
        Done = true;
    }


    public static ConfirmationDialogViewModel ConfirmDelete(string name)
    {
        return new ConfirmationDialogViewModel(
            "Really?",
            $"Are you sure you want to delete '{name}'.\nThis action cannot be undone!",
            IconKind.Warning,
            "Delete",
            "Keep");
    }
}


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
