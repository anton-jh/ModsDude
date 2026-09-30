using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Wpf.Profiles.Editor;

/// <summary>
/// A list's sort as its controls see it: chips for the kind, a dropdown for the attribute, an arrow for
/// the direction. Picking an attribute switches to the attribute sort; picking a chip clears it; each
/// sort opens in its own default direction.
/// </summary>
public sealed partial class ListSortViewModel(ModListSortKind initial, string dateLabel, string dateTooltip) : ObservableObject
{
    private ModListSort _sort = ModListSort.Default(initial);


    public event Action? Changed;


    public string DateLabel { get; } = dateLabel;
    public string DateTooltip { get; } = dateTooltip;

    public ModListSort Sort => _sort;

    public ModListSortKind Kind
    {
        get => _sort.Kind;
        set
        {
            if (value != _sort.Kind)
            {
                Set(ModListSort.Default(value, _sort.Attribute));
            }
        }
    }

    public string? Attribute
    {
        get => _sort.Kind is ModListSortKind.Attribute ? _sort.Attribute : null;
        set
        {
            if (value is not null && value != Attribute)
            {
                Set(ModListSort.Default(ModListSortKind.Attribute, value));
            }
        }
    }

    public bool Ascending => _sort.Ascending;

    public string DirectionText => _sort.DirectionText;


    [RelayCommand]
    private void ToggleDirection() => Set(_sort.Reversed());

    private void Set(ModListSort sort)
    {
        _sort = sort;

        OnPropertyChanged(string.Empty);

        Changed?.Invoke();
    }
}
