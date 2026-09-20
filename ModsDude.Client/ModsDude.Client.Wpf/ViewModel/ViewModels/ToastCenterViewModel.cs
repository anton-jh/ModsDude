using ModsDude.Client.Wpf.ViewModel.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// What the shell draws along the bottom edge: the toasts that are up, oldest at the top.
/// </summary>
/// <remarks>
/// <para>
/// <b>A few at a time.</b> Past <see cref="MaxVisible"/> the oldest goes, rather than the stack growing
/// up the window - a burst of confirmations from one gesture is only ever worth the last few, and a
/// column of toasts is the notice column with a timer.
/// </para>
/// <para>
/// <b>Time on screen follows length.</b> Somebody has to be able to read it, and a sentence that
/// names two folders is not a sentence that reads in three seconds. A warning is given half again,
/// and anything with a link on it long enough to decide about.
/// </para>
/// </remarks>
public sealed class ToastCenterViewModel : IToastService
{
    private const int MaxVisible = 3;


    /// <summary>What is on screen. Only ever touched on the UI thread.</summary>
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];


    public IToast Show(string message, ToastSeverity severity = ToastSeverity.Info, params ToastAction[] actions)
    {
        var toast = new ToastViewModel(message, severity, actions, LifetimeFor(message, severity, actions.Length > 0), Remove);

        OnUiThread(() => Add(toast));

        return toast;
    }


    private void Add(ToastViewModel toast)
    {
        // The same thing said again is one toast with its clock restarted. Pressing the same button
        // three times should not stack three identical cards.
        if (toast.HasActions is false)
        {
            var same = Toasts.FirstOrDefault(x =>
                x.HasActions is false && x.Severity == toast.Severity && x.Message == toast.Message);

            if (same is not null)
            {
                same.Restart();

                return;
            }
        }

        Toasts.Add(toast);
        toast.Start();

        while (Toasts.Count > MaxVisible)
        {
            Toasts[0].Dismiss();
        }
    }

    private void Remove(ToastViewModel toast)
    {
        OnUiThread(() => Toasts.Remove(toast));
    }

    private static TimeSpan LifetimeFor(string message, ToastSeverity severity, bool hasActions)
    {
        var seconds = Math.Clamp(3 + message.Length * 0.05, 4, 10);

        if (severity is ToastSeverity.Warning)
        {
            seconds *= 1.5;
        }

        if (hasActions)
        {
            seconds = Math.Max(seconds, 15);
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static void OnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();

            return;
        }

        dispatcher.InvokeAsync(action);
    }
}
