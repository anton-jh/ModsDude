using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModsDude.Client.Wpf.Shell.Modals;

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
    /// <b>Only for an answer with no side effect</b>: closing changes nothing, and the caller goes on
    /// as if the modal had never been asked. Where the answer is itself a decision the caller acts on
    /// (skip these mods, leave them out, load theirs), Escape does nothing and only its button gives it.
    /// The corner close button and a click outside go through here too.
    /// </para>
    /// <para>
    /// <b>Each modal answers for itself, because only it knows which of its buttons means "no".</b>
    /// Cancel, Close and Dismiss are all that answer in different modals, and a shell reaching
    /// in to press whichever button is on the right would eventually press a destructive one.
    /// </para>
    /// <para>
    /// <b>The answer is the modal's own command, not the method behind it</b>, so a confirmation the
    /// modal would refuse from the button is refused from the keyboard too - see
    /// <see cref="TryAccept"/>, where that matters most.
    /// </para>
    /// <para>
    /// These are asked by the shell, once, wherever focus is. See <c>MainWindow.OnPreviewKeyDown</c>.
    /// </para>
    /// </remarks>
    /// <returns>Whether the key was used, which is what stops it reaching the page behind.</returns>
    public virtual bool TryCancel() => false;

    /// <summary>
    /// What Enter does here, or false where nothing does.
    /// </summary>
    /// <remarks>
    /// <b>Routed through the command, so a modal that cannot be confirmed is not confirmed.</b> A
    /// publish with no name and a rename with no new one both refuse their own button, and Enter has
    /// to mean exactly what the button means or it is a second, laxer way in.
    /// </remarks>
    /// <inheritdoc cref="TryCancel" path="/returns"/>
    public virtual bool TryAccept() => false;

    /// <summary>
    /// Whether the corner close button and a click outside close this modal, both through
    /// <see cref="TryCancel"/>. Only for a modal whose cancel loses nothing and decides nothing.
    /// </summary>
    public virtual bool IsDismissable => false;

    /// <summary>What Ctrl+F does here, if anything.</summary>
    public virtual void Find()
    {
    }

    /// <inheritdoc cref="TryCancel" path="/returns"/>
    public bool TryDismiss() => IsDismissable && TryCancel();


    /// <summary>
    /// Ends the modal without an answer, for a shell that is throwing away the session it belongs to.
    /// Whatever it would have returned stays at its default, which every modal reads as "no".
    /// </summary>
    public virtual void Abandon() => Done = true;


    /// <summary>
    /// Presses one of this modal's own buttons, if it would let itself be pressed.
    /// </summary>
    /// <remarks>
    /// <b>Through the command rather than round it.</b> Every validity rule these modals have is
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
