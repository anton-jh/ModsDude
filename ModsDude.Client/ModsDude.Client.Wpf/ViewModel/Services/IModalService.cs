using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Services;

public interface IModalService
{
    public Task Show(ModalViewModel modal);
}
