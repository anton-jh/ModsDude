using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Wpf.ViewModel.Pages;
using System.ComponentModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

public partial class MenuItemViewModel 
    : ObservableObject, IDisposable
{
    private readonly Func<PageViewModel> _getPage;
    private readonly INotifyPropertyChanged? _source;
    private readonly Func<string>? _getTitle;
    private readonly string? _propertyName;


    public MenuItemViewModel(
        string title,
        Func<PageViewModel> getPage,
        INotifyPropertyChanged? source = null,
        Func<string>? titleSelector = null,
        string? propertyName = null)
    {
        Title = title;
        _getPage = getPage;

        _source = source;
        _getTitle = titleSelector;
        _propertyName = propertyName;

        if (_source is not null && _getTitle is not null)
        {
            _source.PropertyChanged += OnSourcePropertyChanged;
            UpdateTitle();
        }
    }

    
    [ObservableProperty]
    private string _title = "";


    public virtual PageViewModel GetPage()
    {
        return _getPage.Invoke();
    }

    public void Dispose()
    {
        _source?.PropertyChanged -= OnSourcePropertyChanged;
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_propertyName is null || e.PropertyName == _propertyName || string.IsNullOrEmpty(e.PropertyName))
        {
            UpdateTitle();
        }
    }

    private void UpdateTitle()
    {
        if (_source != null && _getTitle != null)
        {
            Title = _getTitle.Invoke();
        }
    }
}
