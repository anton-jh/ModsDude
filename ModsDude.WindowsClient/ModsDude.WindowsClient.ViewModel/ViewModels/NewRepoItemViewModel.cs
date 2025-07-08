using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.WindowsClient.Utilities.GenericFactories;
using ModsDude.WindowsClient.ViewModel.Pages;
using System.ComponentModel;

namespace ModsDude.WindowsClient.ViewModel.ViewModels;
public partial class NewRepoItemViewModel
    : ObservableObject, IMenuItemViewModel
{
    private readonly CreateRepoPageViewModel _page;


    public NewRepoItemViewModel(IFactory<CreateRepoPageViewModel> createRepoPageViewModelFactory)
    {
        _page = createRepoPageViewModelFactory.Create();
        _page.PropertyChanged += Page_PropertyChanged;
    }


    [ObservableProperty]
    private string _title = "New repo";


    public PageViewModel GetPage()
    {
        return _page;
    }

    private void Page_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CreateRepoPageViewModel.Name))
        {
            Title = _page.Name;
        }
    }
}
