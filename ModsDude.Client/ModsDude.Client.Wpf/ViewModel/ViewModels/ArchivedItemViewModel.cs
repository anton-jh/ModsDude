using CommunityToolkit.Mvvm.Input;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>One row of an archive: what it was called, when it was put away, and the two verbs.</summary>
public partial class ArchivedItemViewModel
{
    private readonly Func<ArchivedItemViewModel, Task> _restore;
    private readonly Func<ArchivedItemViewModel, Task> _delete;


    public ArchivedItemViewModel(
        Guid id,
        string name,
        DateTime? archivedAt,
        bool canManage,
        Func<ArchivedItemViewModel, Task> restore,
        Func<ArchivedItemViewModel, Task> delete)
    {
        Id = id;
        Name = name;
        CanManage = canManage;

        _restore = restore;
        _delete = delete;

        // Local, and to the minute: it is read to tell two rows of the same name apart, which is a
        // job for something a person can compare at a glance.
        ArchivedText = archivedAt is DateTime moment
            ? $"Archived {moment.ToLocalTime():g}"
            : "Archived";
    }


    public Guid Id { get; }
    public string Name { get; }
    public string ArchivedText { get; }
    public bool CanManage { get; }


    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task Restore() => _restore(this);

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task Delete() => _delete(this);
}
