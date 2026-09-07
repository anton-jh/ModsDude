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
        bool canRestore,
        bool canDelete,
        Func<ArchivedItemViewModel, Task> restore,
        Func<ArchivedItemViewModel, Task> delete)
    {
        Id = id;
        Name = name;
        CanRestore = canRestore;
        CanDelete = canDelete;

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
    /// <summary>
    /// Whether bringing it back is on offer. A lower bar than deleting, and deliberately: restoring
    /// undoes an archive, and it is the same level as the archiving it undoes.
    /// </summary>
    public bool CanRestore { get; }

    /// <summary>Whether losing it for good is on offer. Admin, everywhere, for all three kinds.</summary>
    public bool CanDelete { get; }

    /// <summary>
    /// Whether a link picked this row out. Set once, when the page is opened by a link into
    /// something archived; a later refresh clears it.
    /// </summary>
    public bool IsHighlighted { get; init; }

    /// <summary>
    /// Four digits shown beside the name where another row here reads the same, and null otherwise.
    /// <see cref="ArchivedText"/> already separates two rows, but it separates them by when somebody
    /// happened to archive each - the tag is the one the repo carries everywhere else too, so a row
    /// here and a sidebar entry can be recognised as the same repo.
    /// </summary>
    public string? Tag { get; init; }

    public bool HasTag => Tag is not null;


    [RelayCommand(CanExecute = nameof(CanRestore))]
    private Task Restore() => _restore(this);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task Delete() => _delete(this);
}
