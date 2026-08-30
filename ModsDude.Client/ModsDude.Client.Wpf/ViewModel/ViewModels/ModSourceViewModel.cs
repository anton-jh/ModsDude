using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One place mods are looked for, with the checkbox that takes it in or out of the merged list.
/// </summary>
/// <remarks>
/// A source that could not be read is bad on its own - an unplugged drive marks one row here rather
/// than failing the list - which is the whole reason the catalog reports failure per source instead
/// of throwing. See docs/09-mod-catalog.md#the-source-list.
/// </remarks>
public partial class ModSourceViewModel : ObservableObject
{
    private readonly Action<ModSourceViewModel, bool> _onEnabledChanged;

    /// <summary>
    /// Set while the initial value is being written, so building the list does not read as the user
    /// having clicked every checkbox in it.
    /// </summary>
    private readonly bool _initialized;


    public ModSourceViewModel(ModSourceStatus status, Action<ModSourceViewModel, bool> onEnabledChanged)
    {
        _onEnabledChanged = onEnabledChanged;

        Source = status.Source;
        Error = status.Error;
        ModCount = status.ModCount;

        _isEnabled = status.IsEnabled;
        _initialized = true;
    }


    public ModSource Source { get; }

    public string Name => Source.Name;
    public string Path => Source.Path;

    /// <summary>Only an ad-hoc source can be taken away; the standing ones are switched off instead.</summary>
    public bool IsAdHoc => Source.Kind is ModSourceKind.AdHoc;

    public string? Error { get; }
    public int ModCount { get; }

    public bool HasFailed => Error is not null;

    public string CountText => HasFailed
        ? "Could not be read"
        : ModCount == 1 ? "1 mod" : $"{ModCount} mods";

    public string KindText => Source.Kind switch
    {
        ModSourceKind.Instance => "Game install",
        ModSourceKind.Downloads => "Downloads",
        _ => "Added this session"
    };

    [ObservableProperty]
    private bool _isEnabled;


    partial void OnIsEnabledChanged(bool value)
    {
        if (_initialized)
        {
            _onEnabledChanged(this, value);
        }
    }
}
