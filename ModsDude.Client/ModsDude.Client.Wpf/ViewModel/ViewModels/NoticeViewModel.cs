using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Notices;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One card in the column.
/// </summary>
/// <remarks>
/// <para>
/// <b>It outlives the notice it draws.</b> The column rebuilds from scratch on every drift check -
/// several times a minute while somebody alt-tabs - and a card replaced wholesale would lose the
/// expansion the user just clicked and the status line of the re-apply it is running. So the centre
/// reconciles by <see cref="Key"/> and calls <see cref="Update"/>, and this holds the state that is
/// the user's rather than the check's.
/// </para>
/// <para>
/// Expanded to start where it is the first card or a critical one, collapsed otherwise. A column of
/// eight open cards after one alt-tab is the wall the single drift card was right to be afraid of;
/// the answer is not fewer notices, it is that only what is at stake is open.
/// </para>
/// </remarks>
public partial class NoticeViewModel : ObservableObject
{
    private readonly Func<Notice, NoticeActionKind, Task> _invoke;
    private readonly Action<Notice> _dismiss;


    public NoticeViewModel(Notice notice, bool expanded, Func<Notice, NoticeActionKind, Task> invoke, Action<Notice> dismiss)
    {
        _invoke = invoke;
        _dismiss = dismiss;

        Key = notice.Key;
        Model = notice;
        IsExpanded = expanded;

        Update(notice);
    }


    public string Key { get; }

    /// <summary>What is being drawn. Replaced by <see cref="Update"/>, never mutated.</summary>
    public Notice Model { get; private set; }


    [ObservableProperty]
    private string _headline = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBody))]
    private string? _body;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFootnote))]
    private string? _footnote;

    [ObservableProperty]
    private NoticeSeverity _severity;

    /// <summary>
    /// The game these cards belong to, drawn as a heading above the first of a run. Null where this
    /// game contributed one card, which names itself in its headline.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGroupHeading))]
    private string? _groupLabel;

    /// <summary>
    /// Whether this card draws the heading. Only the first of a run does - the centre decides, since
    /// it is the only thing that can see the card above.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGroupHeading))]
    private bool _startsGroup;

    [ObservableProperty]
    private bool _canDismiss = true;

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>What an action had to say for itself. Cleared when the card's content changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<NoticeActionViewModel> Actions { get; } = [];

    public bool HasBody => string.IsNullOrWhiteSpace(Body) is false;
    public bool HasFootnote => string.IsNullOrWhiteSpace(Footnote) is false;
    public bool HasStatus => string.IsNullOrWhiteSpace(Status) is false;
    public bool HasGroupHeading => StartsGroup && string.IsNullOrWhiteSpace(GroupLabel) is false;
    public bool HasActions => Actions.Count > 0;


    /// <summary>
    /// Redraws this card for a rebuilt notice under the same key.
    /// </summary>
    /// <remarks>
    /// The buttons are rebuilt only when their labels actually changed, because replacing the
    /// collection while one of them is mid-click is how a running re-apply loses the command it is
    /// running on.
    /// </remarks>
    public void Update(Notice notice)
    {
        var changed = Model.Signature != notice.Signature;

        Model = notice;

        Headline = notice.Headline;
        Body = notice.Body;
        Footnote = notice.Footnote;
        Severity = notice.Severity;
        GroupLabel = notice.GroupLabel;
        CanDismiss = notice.CanDismiss;

        // A card that now says something else is not reporting the outcome of the last thing anybody
        // pressed on it.
        if (changed)
        {
            Status = null;
        }

        if (Actions.Select(x => x.Label).SequenceEqual(notice.Actions.Select(x => x.Label)) is false)
        {
            Actions.Clear();

            foreach (var action in notice.Actions)
            {
                Actions.Add(new(action, this));
            }

            OnPropertyChanged(nameof(HasActions));
        }
    }

    /// <summary>Runs one of this card's actions, with the card marked busy while it goes.</summary>
    public async Task InvokeAsync(NoticeActionKind kind)
    {
        IsBusy = true;

        try
        {
            await _invoke(Model, kind);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ReportStatus(string? status) => Status = status;


    [RelayCommand]
    private void Dismiss() => _dismiss(Model);

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = IsExpanded is false;
}


/// <summary>
/// One button on one card.
/// </summary>
/// <remarks>
/// Its own view model rather than a command on the card taking a parameter, because a card can offer
/// two of these and WPF binding a per-item command through a parent's DataContext is the kind of
/// markup that quietly stops working when the template moves.
/// </remarks>
public partial class NoticeActionViewModel(NoticeAction action, NoticeViewModel owner) : ObservableObject
{
    public string Label { get; } = action.Label;

    /// <summary>Drawn as the accent button. At most one per card - see <see cref="NoticeAction.IsPrimary"/>.</summary>
    public bool IsPrimary { get; } = action.IsPrimary;

    /// <summary>
    /// The plain button, which is every action that is not leading. Declared as its own flag because a
    /// WPF style cannot set the Style property, so the accent cannot be moved onto a button by a
    /// trigger - the template draws both and shows exactly one.
    /// </summary>
    public bool IsSecondary => IsPrimary is false;


    [RelayCommand]
    private Task Invoke() => owner.InvokeAsync(action.Kind);
}
