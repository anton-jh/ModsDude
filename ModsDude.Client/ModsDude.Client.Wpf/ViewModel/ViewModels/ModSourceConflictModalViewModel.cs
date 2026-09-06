using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Asks, once per import, which file to keep where two sources hold genuinely different builds under
/// one mod and version.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only ever reached for files that differ.</b> Identical copies in two folders are the ordinary
/// case and are never asked about - there is nothing to choose between them, so one is taken and
/// nothing is said. What is on screen here are archives whose bytes disagree while claiming the same
/// id and the same version string, which the repo has no way to hold both of.
/// </para>
/// <para>
/// <b>The copies not chosen go to the Recycle Bin</b>, and that is said on the dialog rather than
/// afterwards. It is the whole reason the choice is worth making: leaving them means being asked the
/// same question on every future import, by two files that will never stop disagreeing.
/// </para>
/// <para>
/// Dismissing skips the versions listed here and lets the rest of the import finish, and skipping
/// recycles nothing. Same bargain as the version arbitration dialog: one undecidable mod is one
/// mod's problem, and losing a two-thousand-mod batch over it is not a trade anybody would make.
/// </para>
/// </remarks>
public partial class ModSourceConflictModalViewModel : ModalViewModel
{
    public ModSourceConflictModalViewModel(IReadOnlyList<ModSourceConflict> conflicts)
    {
        Mods = [.. conflicts.Select(x => new ModSourceConflictItemViewModel(x))];
    }


    public ObservableCollection<ModSourceConflictItemViewModel> Mods { get; }

    public string Title => Mods.Count == 1
        ? "Which copy of this mod?"
        : $"Which copies of these {Mods.Count} mods?";

    public string Message =>
        "These are different files claiming the same mod and the same version. Only one can be in the "
        + "repo, so pick the one to keep. The newest is offered first.";

    /// <summary>
    /// Said before the choice rather than reported after it, because it is what the choice costs.
    /// </summary>
    public string RecycleWarning =>
        "The copies you do not pick go to the Recycle Bin, once the mods have imported. Nothing is "
        + "removed if the import does not get that far.";

    public string SkipMessage => Mods.Count == 1
        ? "Skipping leaves this mod unimported and removes nothing. The rest of the import carries on."
        : $"Skipping leaves these {Mods.Count} mods unimported and removes nothing. The rest of the import carries on.";

    /// <summary>
    /// Which file to import per version, or null where the user declined to say. A null answer skips
    /// exactly the versions this dialog was asking about.
    /// </summary>
    public IReadOnlyDictionary<ModVersionIdentity, string>? Result { get; private set; }


    [RelayCommand]
    private void Confirm()
    {
        Result = Mods.ToDictionary(x => x.Identity, x => x.SelectedKey);
        Done = true;
    }

    [RelayCommand]
    private void Skip()
    {
        Result = null;
        Done = true;
    }
}


/// <summary>One version, and the files that disagree about it.</summary>
public sealed class ModSourceConflictItemViewModel
{
    public ModSourceConflictItemViewModel(ModSourceConflict conflict)
    {
        Identity = conflict.Version.Identity;
        Name = conflict.Version.Name;
        Version = conflict.Version.VersionId.Value;

        // Already newest-first from the resolver, which is why the default is the first one: the
        // copy most recently put there is the one somebody just downloaded, far more often than not.
        Candidates = [.. conflict.Candidates.Select(x => new ModFileCandidateViewModel(x, Select))];

        Candidates[0].IsSelected = true;
    }


    public ModVersionIdentity Identity { get; }
    public string Name { get; }
    public string Version { get; }

    public IReadOnlyList<ModFileCandidateViewModel> Candidates { get; }

    /// <summary>Always exactly one, because <see cref="Select"/> is what maintains that.</summary>
    public string SelectedKey => Candidates.First(x => x.IsSelected).Key;


    /// <summary>
    /// The mutual exclusion, held here rather than by <c>RadioButton.GroupName</c>. A group name is
    /// resolved across the whole window, so several of these lists on one dialog would share one
    /// group and a second mod's choice would clear the first mod's.
    /// </summary>
    private void Select(ModFileCandidateViewModel chosen)
    {
        foreach (var candidate in Candidates)
        {
            if (ReferenceEquals(candidate, chosen) is false)
            {
                candidate.Clear();
            }
        }
    }
}


/// <summary>
/// One distinct file, described by what tells two builds of one mod apart: when it was written, how
/// big it is, and where it is. The hash is there for the one case where nothing else differs.
/// </summary>
public class ModFileCandidateViewModel : ObservableObject
{
    private readonly Action<ModFileCandidateViewModel> _onSelected;


    public ModFileCandidateViewModel(ModFileCandidate candidate, Action<ModFileCandidateViewModel> onSelected)
    {
        _onSelected = onSelected;

        Key = candidate.Key;

        var written = ModOccurrenceResolver.LastWritten(candidate);

        Headline = written == DateTime.MinValue
            ? $"{candidate.FileLength:N0} bytes"
            : $"{written.ToLocalTime():g} · {candidate.FileLength:N0} bytes";

        // Enough hash to tell two rows apart on screen, and no more: this is a label, not something
        // anybody is going to compare by hand.
        ShortHash = candidate.ContentHash is { Length: >= 8 } hash ? hash[..8] : "";

        // Every place these exact bytes were found. More than one is ordinary and means nothing is
        // lost by picking this row - the copies of it stay where they are.
        Locations = [.. candidate.Occurrences.Select(x => $"{x.Source.Name} · {x.FilePath}")];
    }


    /// <summary>What the answer names, and the only thing the import reads back.</summary>
    public string Key { get; }

    public string Headline { get; }
    public string ShortHash { get; }

    public IReadOnlyList<string> Locations { get; }

    private bool _isSelected;

    /// <summary>
    /// Hand-written rather than generated, because selecting one row has to clear the others and the
    /// clearing must not fire the same notification back.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();

            if (value)
            {
                _onSelected(this);
            }
        }
    }


    /// <summary>Deselects without telling the owner, which is already doing the telling.</summary>
    public void Clear()
    {
        if (_isSelected is false)
        {
            return;
        }

        _isSelected = false;
        OnPropertyChanged(nameof(IsSelected));
    }
}

