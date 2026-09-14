using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One place versions come from, as a toggle chip with a count on it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A chip rather than a row in a pane.</b> Three shapes were tried: an expander whose collapsed
/// state hid the one control that explains an empty left list, a fixed pane costing a quarter of the
/// left column permanently, and this. A chip row is the only one of the three that is both always
/// visible and nearly free, and it is honest about what these things are - every source, the repo
/// included, is a filter over one list.
/// </para>
/// <para>
/// A source that could not be read is bad on its own - an unplugged drive marks one chip rather than
/// failing the list - which is the whole reason the catalog reports failure per source instead of
/// throwing. The failure colours the chip and the reason is in its tooltip, because a second line
/// under it would cost every chip the height of the worst one.
/// See docs/09-mod-catalog.md#the-source-chips.
/// </para>
/// </remarks>
public partial class ModSourceViewModel : ObservableObject
{
    private readonly Action<ModSourceViewModel, bool> _onEnabledChanged;

    /// <summary>
    /// Set while the initial value is being written, so building the row does not read as the user
    /// having clicked every chip in it.
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

    /// <summary>A folder the user added for this session. Never persisted, and removable.</summary>
    public bool IsAdHoc => Source.Kind is ModSourceKind.AdHoc;

    /// <summary>
    /// Whether this source is one the user added rather than one that is always there. Ad-hoc
    /// folders and profiles carry their own ⨯; the standing ones are switched off instead.
    /// </summary>
    public bool CanRemove => Source.Kind is ModSourceKind.AdHoc or ModSourceKind.Profile;

    /// <summary>
    /// Whether this chip is the repo rather than somewhere to look. It has no scan, no failure and
    /// nothing to rescan, and what it contributes is every version the repo has registered.
    /// </summary>
    public bool IsRepo => Source.Kind is ModSourceKind.Repo;

    /// <summary>
    /// Whether this chip is another profile. Like the repo it costs no scan - a profile's pins are
    /// registered versions, so the catalog already holds every one of them.
    /// </summary>
    public bool IsProfile => Source.Kind is ModSourceKind.Profile;

    public string? Error { get; }
    public int ModCount { get; }

    public bool HasFailed => Error is not null;

    /// <summary>What the management page's source pane says under the name. A line, with room for one.</summary>
    public string CountText => HasFailed
        ? "Could not be read"
        : IsRepo
            ? ModCount == 1 ? "1 registered version" : $"{ModCount} registered versions"
            : ModCount == 1 ? "1 mod" : $"{ModCount} mods";

    /// <inheritdoc cref="CountText"/>
    public string KindText => Source.Kind switch
    {
        ModSourceKind.Repo => "This repo",
        ModSourceKind.Game => "Game install",
        ModSourceKind.Downloads => "Downloads",
        ModSourceKind.Profile => "Another profile",
        _ => "Added this session"
    };

    /// <summary>
    /// The number on the chip, and nothing else - the units are what the chip's own name is for, and
    /// at four chips wide there is no room to spell them twice.
    /// </summary>
    public string ChipCountText => HasFailed ? "!" : ModCount.ToString("N0");

    /// <summary>
    /// The whole of what the chip cannot fit: what kind of thing it is, where it is, and why it is
    /// empty where it failed.
    /// </summary>
    public string Tooltip
    {
        get
        {
            var what = Source.Kind switch
            {
                ModSourceKind.Repo => "Everything this repo has registered.",
                ModSourceKind.Game => $"A mod folder this game reaches.\n{Path}",
                ModSourceKind.Downloads => $"The system Downloads folder.\n{Path}",
                ModSourceKind.Profile => $"What this profile pins. {Path}",
                _ => $"Added for this session only.\n{Path}"
            };

            return HasFailed ? $"{what}\n\nCould not be read: {Error}" : what;
        }
    }

    public string RemoveTooltip => IsProfile ? "Stop reading this profile" : "Stop reading this folder";

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
