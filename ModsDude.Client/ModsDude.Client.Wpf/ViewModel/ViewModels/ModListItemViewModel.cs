using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One mod version as it appears in a list. Bound to an implicit template in App.xaml, so any items
/// control fed these renders the same row.
/// </summary>
/// <remarks>
/// Wraps a <see cref="CatalogModVersion"/> rather than a local mod, which is what lets one row type
/// serve a list that mixes what is on disk, what the repo holds, and what is both.
/// </remarks>
public partial class ModListItemViewModel : ObservableObject, ILazyLoadable
{
    private readonly Guid _repoId;
    private readonly IModImageProvider _imageProvider;
    private readonly IModImagerySource _imagerySource;
    private readonly IModalService _modalService;

    private Task<ModVersionImagery>? _imagery;
    private bool _thumbnailRequested;


    public ModListItemViewModel(
        Guid repoId,
        CatalogModVersion mod,
        IModImageProvider imageProvider,
        IModImagerySource imagerySource,
        IModalService modalService)
    {
        Mod = mod;
        _repoId = repoId;
        _imageProvider = imageProvider;
        _imagerySource = imagerySource;
        _modalService = modalService;

        ShortDescription = BuildShortDescription(mod.Name, mod.Description);
        Initials = BuildInitials(mod.Name);
    }


    public CatalogModVersion Mod { get; }

    public string Id => Mod.ModId.Value;
    public string Name => Mod.Name;
    public string Version => Mod.VersionId.Value;
    public string? Author => Mod.Author;
    public string ShortDescription { get; }

    /// <summary>Stands in for the icon while it loads, and for mods that ship without one.</summary>
    public string Initials { get; }

    [ObservableProperty]
    private ImageSource? _thumbnail;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Set to false where the list is for browsing rather than picking.</summary>
    [ObservableProperty]
    private bool _isSelectable = true;

    /// <summary>
    /// The sources the version was found in - the same mod is usually installed in several. Left
    /// unset where naming them would say nothing, such as a single enabled source.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstances))]
    private string? _instances;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private ModDisplayStatus _status = ModDisplayStatus.None;


    public bool HasStatus => Status is not ModDisplayStatus.None;

    public bool HasInstances => string.IsNullOrWhiteSpace(Instances) is false;

    public string StatusText => Status switch
    {
        ModDisplayStatus.New => "New",
        ModDisplayStatus.UpdateAvailable => "Update",
        ModDisplayStatus.AlreadyInRepo => "In repo",
        _ => string.Empty
    };


    public bool Matches(string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return true;
        }

        var term = searchTerm.Trim();

        return Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || Id.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (Author?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>
    /// Reads the icon the first time the row is shown. Everything here stays cold until then - with
    /// a few thousand mods in a folder, unpacking every archive up front would cost minutes of
    /// startup and hundreds of megabytes.
    /// </summary>
    public async Task LoadAsync()
    {
        if (_thumbnailRequested)
        {
            return;
        }

        _thumbnailRequested = true;

        var imagery = await ResolveImageryAsync();

        if (imagery.Icon is null)
        {
            // Initials, exactly as for a local mod that ships without an icon.
            return;
        }

        Thumbnail = await _imageProvider.GetAsync(imagery.Icon, IModImageProvider.ThumbnailSize, CancellationToken.None);
    }


    [RelayCommand]
    private async Task ShowDetails()
    {
        await _modalService.Show(new ModDetailsModalViewModel(Mod, await ResolveImageryAsync(), _imageProvider));
    }

    /// <summary>
    /// Where this row's imagery comes from, resolved once and shared with the details dialog. For a
    /// registered version that has none and whose file is here, this is what generates and uploads
    /// it - the client that noticed the gap is the one best placed to close it, for everyone.
    /// </summary>
    private Task<ModVersionImagery> ResolveImageryAsync()
    {
        return _imagery ??= ResolveAsync();


        async Task<ModVersionImagery> ResolveAsync()
        {
            try
            {
                return await _imagerySource.GetAsync(_repoId, Mod, CancellationToken.None);
            }
            catch (Exception)
            {
                // There is no user action to suggest and an error per row would be unusable, so a
                // row whose imagery could not be reached renders as initials.
                return ModVersionImagery.None;
            }
        }
    }


    /// <summary>
    /// Descriptions run to hundreds of lines and usually open by repeating the mod's own name, so
    /// take the first line that actually says something new.
    /// </summary>
    private static string BuildShortDescription(string name, string description)
    {
        var lines = description
            .Split('\n')
            .Select(x => Regex.Replace(x, @"\s+", " ").Trim())
            .Where(x => string.IsNullOrEmpty(x) is false);

        var line = lines.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase) is false)
            ?? string.Empty;

        return line;
    }

    private static string BuildInitials(string name)
    {
        var initials = name
            .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.FirstOrDefault(char.IsLetterOrDigit))
            .Where(x => x != default)
            .Take(2);

        return string.Concat(initials).ToUpperInvariant();
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public ModListItemViewModel Create(Guid repoId, CatalogModVersion mod)
            => ActivatorUtilities.CreateInstance<ModListItemViewModel>(serviceProvider, repoId, mod);
    }
}
