using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Client.Core.Users;
using ModsDude.Client.Wpf.ViewModel.Services;
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
    [NotifyPropertyChangedFor(nameof(Initial))]
    private string _title = "";

    /// <summary>
    /// The glyph drawn ahead of the title, from <see cref="MenuIcons"/>. Empty rather than null on an
    /// entry without one: the icon cell has a fixed width, so an entry that has no picture still
    /// starts its text where every other entry does.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayIcon))]
    private string _icon = "";

    /// <summary>
    /// Where the profile this entry stands for stands against the game following it. None - nearly
    /// always - leaves the entry as it was.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayIcon))]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private ProfileSyncState _syncState;

    /// <summary>Whether <see cref="SyncState"/> is anything to draw.</summary>
    public bool HasStatus => SyncState is not ProfileSyncState.None;

    /// <summary>
    /// The glyph actually drawn: the status where there is one, and otherwise the entry's own. Swapped
    /// rather than added beside it, so every row keeps the one icon cell it always had.
    /// </summary>
    public string DisplayIcon => SyncState switch
    {
        ProfileSyncState.InSync => MenuIcons.SyncInSync,
        ProfileSyncState.Drifted or ProfileSyncState.NotApplied => MenuIcons.SyncWarning,
        ProfileSyncState.Applying => MenuIcons.SyncApplying,
        _ => Icon
    };

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
    /// A few characters drawn dimmed after the title, for an entry that would otherwise read the
    /// same as another one in the same list. Null on every entry that does not need it, which is
    /// almost all of them - see <c>RepoDisplay</c> for why this is decided per list rather than
    /// carried by the thing being named.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTag))]
    [NotifyPropertyChangedFor(nameof(TagText))]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    private string? _tag;

    public bool HasTag => Tag is not null;

    /// <summary>
    /// The tag as it is read - <c>" #1234"</c>, its own separating space included. Empty rather than
    /// null on an entry without one, because it is drawn inline with the title and an empty run
    /// takes no space; nothing has to be hidden.
    /// </summary>
    public string TagText => Tag is null ? "" : $" #{Tag}";

    /// <summary>
    /// Whether the entry names a thing somebody called something - a repo or a profile - rather than a
    /// place the app offers. Such a name can be any length, so its row gets two lines; a fixed label
    /// like Overview does not, and stays one.
    /// </summary>
    public virtual bool IsEntity => false;

    /// <summary>
    /// The one character an entity's square is marked with, in place of a glyph every repo - or every
    /// profile - would share. A name that is all punctuation gets none.
    /// </summary>
    public string Initial => UserDisplay.InitialFor(Title);

    /// <summary>
    /// One tooltip per row, so it does double duty: the reason when there is one, and otherwise the
    /// title, which the sidebar trims to its width and would otherwise leave unreadable. The tag
    /// rides along with it, because a name long enough to be trimmed takes its tag with it.
    /// </summary>
    public string ToolTip => UnavailableReason ?? $"{Title}{TagText}";


    public virtual PageViewModel GetPage()
    {
        return _getPage.Invoke();
    }

    /// <summary>
    /// Gives the entry its glyph, and hands it back so a menu can be written as one list of
    /// expressions - the same shape <see cref="Restrict"/> already has.
    /// </summary>
    public MenuItemViewModel WithIcon(string icon)
    {
        Icon = icon;

        return this;
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
