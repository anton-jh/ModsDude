using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One toast on screen, and the clock that takes it down again.
/// </summary>
/// <remarks>
/// <b>The clock stops while the pointer is on it.</b> A toast that leaves while somebody is reading
/// it, or is reaching for the link on it, is the one way this can be worse than a dialog - so
/// pointing at it is taken as "not yet", and it gets its whole time again once the pointer leaves.
/// </remarks>
public sealed class ToastViewModel : IToast
{
    private readonly Action<ToastViewModel> _remove;
    private readonly TimeSpan _lifetime;

    private DispatcherTimer? _timer;


    public ToastViewModel(
        string message,
        ToastSeverity severity,
        IReadOnlyList<ToastAction> actions,
        TimeSpan lifetime,
        Action<ToastViewModel> remove)
    {
        _lifetime = lifetime;
        _remove = remove;

        Message = message;
        Severity = severity;
        Actions = [.. actions.Select(x => new ToastActionViewModel(x, this))];

        DismissCommand = new RelayCommand(Dismiss);
    }


    public string Message { get; }

    public ToastSeverity Severity { get; }

    public IReadOnlyList<ToastActionViewModel> Actions { get; }

    public bool HasActions => Actions.Count > 0;

    public RelayCommand DismissCommand { get; }


    /// <summary>Starts the clock. On the UI thread, which is where the timer has to be made.</summary>
    internal void Start()
    {
        _timer = new DispatcherTimer { Interval = _lifetime };
        _timer.Tick += (_, _) => Dismiss();
        _timer.Start();
    }

    /// <summary>Gives a toast that has just been said again its whole time back.</summary>
    internal void Restart()
    {
        _timer?.Stop();
        _timer?.Start();
    }

    /// <summary>The pointer is on it: hold the clock.</summary>
    public void Pause() => _timer?.Stop();

    /// <summary>The pointer left: the whole time again, so the last words are as readable as the first.</summary>
    public void Resume() => _timer?.Start();

    public void Dismiss()
    {
        _timer?.Stop();
        _timer = null;

        _remove(this);
    }
}


/// <summary>One link on a toast. Pressing it does its thing and takes the toast down.</summary>
public sealed class ToastActionViewModel
{
    public ToastActionViewModel(ToastAction action, ToastViewModel toast)
    {
        Label = action.Label;

        InvokeCommand = new RelayCommand(() =>
        {
            // Down first, so what the action does - which may be slow, or show a dialog - is not
            // happening under a toast that still says it is on offer.
            toast.Dismiss();

            action.Invoke();
        });
    }

    public string Label { get; }

    public RelayCommand InvokeCommand { get; }
}
