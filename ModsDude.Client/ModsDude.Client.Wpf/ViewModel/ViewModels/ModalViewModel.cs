using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

public partial class ModalViewModel : ObservableObject
{

    public delegate void ModalDoneHandler();
    public event ModalDoneHandler? Completed;


    [ObservableProperty]
    private bool _done;


    /// <summary>
    /// What Escape does here, or false where nothing does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Each dialog answers for itself, because only it knows which of its buttons means "no".</b>
    /// Skip, Cancel, Close and Dismiss are all that answer in different dialogs, and a shell reaching
    /// in to press whichever button is on the right would eventually press a destructive one.
    /// </para>
    /// <para>
    /// <b>The answer is the dialog's own command, not the method behind it</b>, so a confirmation the
    /// dialog would refuse from the button is refused from the keyboard too - see
    /// <see cref="TryAccept"/>, where that matters most.
    /// </para>
    /// <para>
    /// These are asked by the shell, once, rather than by seventeen copies of a <c>KeyBinding</c> on
    /// seventeen <c>UserControl</c>s - which is what was there, and which fired only when focus
    /// happened to be inside the dialog. Nine of them never took focus at all, so their Escape did
    /// nothing whatsoever. See <c>MainWindow.OnPreviewKeyDown</c>.
    /// </para>
    /// </remarks>
    /// <returns>Whether the key was used, which is what stops it reaching the page behind.</returns>
    public virtual bool TryCancel() => false;

    /// <summary>
    /// What Enter does here, or false where nothing does.
    /// </summary>
    /// <remarks>
    /// <b>Routed through the command, so a dialog that cannot be confirmed is not confirmed.</b> A
    /// publish with no name and a rename with no new one both refuse their own button, and Enter has
    /// to mean exactly what the button means or it is a second, laxer way in.
    /// </remarks>
    /// <inheritdoc cref="TryCancel" path="/returns"/>
    public virtual bool TryAccept() => false;


    /// <summary>
    /// Presses one of this dialog's own buttons, if it would let itself be pressed.
    /// </summary>
    /// <remarks>
    /// <b>Through the command rather than round it.</b> Every validity rule these dialogs have is
    /// already a <c>CanExecute</c>, so asking the command is what keeps the keyboard and the button
    /// meaning the same thing - and it keeps that true when somebody adds a rule later without
    /// thinking about Enter.
    /// </remarks>
    /// <returns>Whether it ran, which is the answer both overrides above want.</returns>
    protected static bool Press(IRelayCommand command)
    {
        if (command.CanExecute(null) is false)
        {
            return false;
        }

        command.Execute(null);

        return true;
    }


    partial void OnDoneChanged(bool value)
    {
        if (value == true)
        {
            Completed?.Invoke();
        }
    }
}
