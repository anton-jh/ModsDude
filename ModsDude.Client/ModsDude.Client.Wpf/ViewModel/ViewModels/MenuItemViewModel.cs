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
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    private string _title = "";

    /// <summary>
    /// Whether the entry can be navigated to. False greys it out and refuses the click, rather than
    /// hiding it: an entry that is simply gone tells a user nothing about the level they would need
    /// to ask for.
    /// </summary>
    /// <remarks>
    /// A hint, not a guard. A disabled container refuses mouse and keyboard, but nothing stops code
    /// setting <c>NavigationManager.Selected</c> to it, and the server is the only real authority in
    /// any case - the pages behind these entries keep their own checks.
    /// </remarks>
    [ObservableProperty]
    private bool _isAvailable = true;

    /// <summary>Why the entry is unavailable, shown in its place. Null while it is available.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    private string? _unavailableReason;

    /// <summary>
    /// One tooltip per row, so it does double duty: the reason when there is one, and otherwise the
    /// title, which the sidebar trims to its width and would otherwise leave unreadable.
    /// </summary>
    public string ToolTip => UnavailableReason ?? Title;


    public virtual PageViewModel GetPage()
    {
        return _getPage.Invoke();
    }

    /// <summary>Closes the entry, with the sentence the user sees instead of the page.</summary>
    public MenuItemViewModel Restrict(string reason)
    {
        IsAvailable = false;
        UnavailableReason = reason;

        return this;
    }

    /// <summary>Opens it again, for an entry whose answer arrives after the menu is built.</summary>
    public void Allow()
    {
        IsAvailable = true;
        UnavailableReason = null;
    }

    /// <summary>Closes or opens the entry in one call, for a condition known only at runtime.</summary>
    public MenuItemViewModel RestrictIf(bool isRestricted, string reason)
    {
        if (isRestricted)
        {
            return Restrict(reason);
        }

        Allow();

        return this;
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
