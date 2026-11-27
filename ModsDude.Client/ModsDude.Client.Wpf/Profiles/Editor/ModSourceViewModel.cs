using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Wpf.Profiles.Editor;

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


    /// <param name="canUnload">Whether the chip carries a ✕ that unloads what the source supplied.</param>
    /// <param name="detail">A remote source's state, or what a savegame lists, for the tooltip.</param>
    /// <param name="foundCount">How many of a savegame's listed mods were found, where some were not.</param>
    public ModSourceViewModel(
        ModSourceStatus status,
        Action<ModSourceViewModel, bool> onEnabledChanged,
        bool hasCount = true,
        bool canUnload = false,
        string? detail = null,
        int? foundCount = null)
    {
        _onEnabledChanged = onEnabledChanged;

        Source = status.Source;
        Detail = detail;
        Error = status.Error;
        ModCount = status.ModCount;
        FoundCount = foundCount;
        HasCount = hasCount;
        CanRemove = canUnload;

        _isEnabled = status.IsEnabled;
        _initialized = true;
    }


    public ModSource Source { get; }

    public string Name => Source.Name;
    public string Path => Source.Path;

    /// <summary>Whether this chip is a folder on this machine, which can be opened in Explorer.</summary>
    public bool IsFolder => Source.Kind is ModSourceKind.Game or ModSourceKind.Downloads or ModSourceKind.AdHoc;

    public bool CanRemove { get; }

    /// <summary>
    /// Whether this chip is the repo rather than somewhere to look. It has no scan, no failure and
    /// nothing to rescan, and what it contributes is every version the repo has registered.
    /// </summary>
    public bool IsRepo => Source.Kind is ModSourceKind.Repo;

    /// <summary>Whether this chip is a remote source, whose versions a save downloads.</summary>
    public bool IsRemote => Source.Kind is ModSourceKind.Remote;

    /// <summary>Whether this chip is a list of mods whose files come from other sources: a profile or a savegame.</summary>
    public bool IsList => Source.Kind is ModSourceKind.Profile or ModSourceKind.Savegame;

    public string? Detail { get; }

    public string? Error { get; }
    public int ModCount { get; }
    public int? FoundCount { get; }
    public bool HasCount { get; }

    public bool HasFailed => Error is not null;

    /// <summary>
    /// The number on the chip, and nothing else - the units are what the chip's own name is for, and
    /// at four chips wide there is no room to spell them twice.
    /// </summary>
    public string ChipCountText => HasFailed
        ? "!"
        : HasCount is false
            ? string.Empty
            : FoundCount is int found && found != ModCount
                ? $"{found:N0}/{ModCount:N0}"
                : ModCount.ToString("N0");

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
                ModSourceKind.Repo => "All mods in this repo",
                ModSourceKind.Game => $"Mod folder of this game\n{Path}",
                ModSourceKind.Downloads => $"Downloads folder\n{Path}",
                ModSourceKind.Profile => "Another profile in this repo",
                ModSourceKind.Savegame => $"Savegame on this machine\n{Path}\n\n{Detail}",
                ModSourceKind.Remote => $"Updates from {Name}, downloaded on save\n{Detail}",
                _ => $"Temporary source\n{Path}"
            };

            return HasFailed ? $"{what}\n\nCould not be read: {Error}" : what;
        }
    }

    public string RemoveTooltip => "Unload";

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
