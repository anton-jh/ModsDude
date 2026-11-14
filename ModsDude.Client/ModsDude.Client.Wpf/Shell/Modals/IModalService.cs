
namespace ModsDude.Client.Wpf.Shell.Modals;

public interface IModalService
{
    /// <summary>
    /// Shows <paramref name="modal"/> over whatever modal is already up, and completes once it is done.
    /// The one underneath comes back when it is.
    /// </summary>
    public Task Show(ModalViewModel modal);

    /// <summary>Ends every open modal unanswered, newest first. See <see cref="ModalViewModel.Abandon"/>.</summary>
    public void DismissAll();
}
