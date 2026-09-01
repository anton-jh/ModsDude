using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Text.RegularExpressions;
using System.Windows.Input;
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
    private readonly ILogger<ModListItemViewModel> _logger;
    private readonly IBackgroundProblemReporter _problems;

    private Task<ModVersionImagery>? _imagery;
    private bool _thumbnailRequested;


    public ModListItemViewModel(
        Guid repoId,
        CatalogModVersion mod,
        IModImageProvider imageProvider,
        IModImagerySource imagerySource,
        IModalService modalService,
        ILogger<ModListItemViewModel> logger,
        IBackgroundProblemReporter problems)
    {
        Mod = mod;
        _repoId = repoId;
        _imageProvider = imageProvider;
        _imagerySource = imagerySource;
        _modalService = modalService;
        _logger = logger;
        _problems = problems;

        ShortDescription = BuildShortDescription(mod.Name, mod.Description);
        Initials = BuildInitials(mod.Name);
    }


    public CatalogModVersion Mod { get; }

    public string Id => Mod.ModId.Value;
    public string Name => Mod.Name;
    public string Version => Mod.VersionId.Value;
    public string? Author => Mod.Author;
    public string ShortDescription { get; }

    public bool IsOnServer => Mod.IsOnServer;
    public bool IsLocal => Mod.IsLocal;

    /// <summary>
    /// Version-sensitive, as the adapter derived it from the archive. Shown rather than editable:
    /// an adapter re-derives this from every file, so there is nothing here for a user to override.
    /// The per-profile lock is a different decision on a different page.
    /// </summary>
    public bool IsLocked => Mod.Locked;

    /// <summary>
    /// Two sources hold files claiming this mod and version and disagreeing about them. Surfaced on
    /// the row because the catalog already withholds the stream, so an import would refuse anyway -
    /// a row that looked importable would just spend a round trip to say so.
    /// </summary>
    public bool HasSourceConflict => Mod.HasSourceConflict;

    public string SourceConflictTooltip => "Two sources hold different files for this mod and version. "
        + "Only one can be registered, so disable a source to choose between them:"
        + string.Concat(Mod.FoundIn.Select(x => $"\n{x.Source.Name} - {x.FilePath} ({x.FileLength:N0} bytes)"));

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
    [NotifyPropertyChangedFor(nameof(HasSources))]
    private string? _sources;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ChipText))]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private ModDisplayStatus _status = ModDisplayStatus.None;

    /// <summary>
    /// What a managing page lets this row do. The actions need the mod's siblings and the page's own
    /// refresh, so they belong to the page rather than to the row; null leaves the row read-only,
    /// which is what every other list wants.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActions))]
    private ModRowActions? _actions;


    public bool HasStatus => Status is not ModDisplayStatus.None;

    public bool HasSources => string.IsNullOrWhiteSpace(Sources) is false;

    public bool HasActions => Actions is not null;

    public string StatusText => Status switch
    {
        ModDisplayStatus.New => "New",
        ModDisplayStatus.UpdateAvailable => "Update",
        ModDisplayStatus.AlreadyInRepo => "In repo",
        _ => string.Empty
    };

    /// <summary>
    /// What the row's one chip says: the import, while it has anything to say, and the presence
    /// status otherwise. Stacking the two would make every row two chips wide for the sake of one
    /// moment, and the running import is the more urgent of them.
    /// </summary>
    public string ChipText => HasImportState ? ImportStateText : StatusText;


    #region Import

    /// <summary>
    /// Where this row is in the import that is running. Null once it is over - what the import
    /// concluded is <see cref="ImportOutcome"/>, which outlives the run.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportState))]
    [NotifyPropertyChangedFor(nameof(ImportStateText))]
    [NotifyPropertyChangedFor(nameof(ChipText))]
    [NotifyPropertyChangedFor(nameof(HasImportState))]
    [NotifyPropertyChangedFor(nameof(IsUploading))]
    private ModImportPhase? _importPhase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportState))]
    [NotifyPropertyChangedFor(nameof(ImportStateText))]
    [NotifyPropertyChangedFor(nameof(ChipText))]
    [NotifyPropertyChangedFor(nameof(HasImportState))]
    private ModImportStatus? _importOutcome;

    /// <summary>Zero to one, and only while uploading - the one phase whose length is knowable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportStateText))]
    [NotifyPropertyChangedFor(nameof(ChipText))]
    private double _importProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImportMessage))]
    private string? _importMessage;


    /// <summary>
    /// Four states rather than a bool, because a mod that failed and a mod that was skipped are
    /// different situations: one is worth retrying and one needs a decision first. Rendering them
    /// the same is what makes a two-thousand-row import unreadable.
    /// </summary>
    public ModImportRowState ImportState => ImportOutcome switch
    {
        ModImportStatus.Registered or ModImportStatus.AlreadyRegistered => ModImportRowState.Succeeded,
        ModImportStatus.Failed => ModImportRowState.Failed,
        not null => ModImportRowState.Skipped,
        null => ImportPhase is null ? ModImportRowState.None : ModImportRowState.Running
    };

    public bool HasImportState => ImportState is not ModImportRowState.None;

    public bool HasImportMessage => string.IsNullOrWhiteSpace(ImportMessage) is false;

    public bool IsUploading => ImportPhase is ModImportPhase.Uploading;

    public string ImportStateText => ImportOutcome switch
    {
        ModImportStatus.Registered => "Imported",
        ModImportStatus.AlreadyRegistered => "In repo",
        ModImportStatus.SourceConflict => "Source conflict",
        ModImportStatus.ContentMismatch => "Different file stored",
        ModImportStatus.NeedsArbitration => "Order not settled",
        ModImportStatus.NoLocalFile => "No local file",
        ModImportStatus.Failed => "Failed",
        _ => ImportPhase switch
        {
            ModImportPhase.Queued => "Queued",
            ModImportPhase.Linking => "Preparing",
            ModImportPhase.Uploading => $"Uploading {ImportProgress:P0}",
            ModImportPhase.Registering => "Registering",
            ModImportPhase.PublishingImagery => "Publishing images",
            ModImportPhase.Completed => "Done",
            ModImportPhase.Failed => "Failed",
            ModImportPhase.Skipped => "Skipped",
            _ => string.Empty
        }
    };


    public void Apply(ModImportProgress progress)
    {
        ImportPhase = progress.Phase;
        ImportProgress = progress.TotalBytes > 0
            ? (double)progress.BytesTransferred / progress.TotalBytes
            : 0;

        if (progress.Error is not null)
        {
            ImportMessage = progress.Error;
        }
    }

    public void Apply(ModImportItemResult result)
    {
        ImportOutcome = result.Status;
        ImportPhase = null;

        if (result.Message is not null)
        {
            ImportMessage = result.Message;
        }
    }

    /// <summary>Clears what the last import said, so a second run does not read as the first one.</summary>
    public void ResetImportState()
    {
        ImportPhase = null;
        ImportOutcome = null;
        ImportProgress = 0;
        ImportMessage = null;
    }

    #endregion


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
            catch (Exception exception)
            {
                // There is no user action to suggest and an error per row would be unusable, so a
                // row whose imagery could not be reached renders as initials - and says so once, in
                // the shell notice, however many rows it happened to.
                _logger.LogWarning(
                    exception,
                    "Could not resolve imagery for {ModId} {VersionId}; the row will render as initials.",
                    Mod.ModId.Value, Mod.VersionId.Value);

                _problems.Report(BackgroundProblem.ImageDisplay);

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


/// <summary>
/// What a page lets a row do to the repo. Each command takes the row it was invoked on, so one set
/// serves every row rather than being rebuilt per item.
/// </summary>
public sealed record ModRowActions(
    ICommand ReorderVersions,
    ICommand DeleteVersion,
    ICommand DeleteMod,
    string? Restriction = null)
{
    /// <summary>
    /// Why all three are refused, shown on the buttons themselves. Null where they are allowed - the
    /// commands are then enabled and there is nothing to explain.
    /// </summary>
    public bool HasRestriction => Restriction is not null;
}


/// <summary>
/// How a row reads during and after an import. Deliberately coarser than
/// <see cref="ModImportStatus"/>: the row needs to be scannable down a list of two thousand, and the
/// exact reason belongs in the message.
/// </summary>
public enum ModImportRowState
{
    None,
    Running,
    Succeeded,

    /// <summary>Nothing was registered, and something has to be decided before it can be.</summary>
    Skipped,

    Failed
}
