using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;
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
public partial class ModListItemViewModel : ObservableObject, ILazyLoadable, ISelectableRow
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

    /// <summary>
    /// Whether the chip has anything to say. Every version out of the catalog does; a row standing in
    /// for a file that is named rather than versioned - an unrecognised file in a mod folder - does
    /// not, and an empty pill on it would read as a missing value.
    /// </summary>
    public bool HasVersion => string.IsNullOrEmpty(Version) is false;

    /// <summary>
    /// Whether the row draws its version as a chip. Off in the profile editor, whose rows all carry a
    /// version selector that says the same thing - and says it for the version the row is showing,
    /// which is the one that can be changed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsVersionChip))]
    private bool _showVersion = true;

    public bool ShowsVersionChip => ShowVersion && HasVersion;

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

    public string SourceConflictTooltip => "Two sources hold files of different sizes for this mod and "
        + "version. Only one can be registered, and importing will ask which:"
        + string.Concat(Mod.FoundIn.Select(x => $"\n{x.Source.Name} - {x.FilePath} ({x.FileLength:N0} bytes)"));

    /// <summary>
    /// The order between this version and what the repo holds is not settled - a comparer abstention
    /// against the repo's own newest, which is exactly the pair the import-time arbitration dialog
    /// exists for. Set by the page from <c>ModVersionSet.CouldNotCompareToNewest</c>, which is where
    /// the comparison actually lives: this row only renders the fact.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSecondaryChip))]
    [NotifyPropertyChangedFor(nameof(SecondaryChipText))]
    [NotifyPropertyChangedFor(nameof(SecondaryChipTooltip))]
    private bool _orderNotSettled;

    /// <summary>
    /// The small chip beside the main status one, which a source conflict already used alone. Both
    /// mean "this row will ask you something at save", so they share the slot rather than each
    /// costing the row a column of its own; a conflict is the more urgent of the two and wins when a
    /// version somehow manages to be both.
    /// </summary>
    public bool HasSecondaryChip => HasSourceConflict || OrderNotSettled;

    /// <summary>
    /// "New?" rather than a word about ordering - the reader does not need to know a comparer
    /// abstained, they need to know this might be the version they came here to add. The question
    /// mark is the point: the row is an invitation, not a warning, which is also why it never
    /// replaces the main chip's plain "New".
    /// </summary>
    public string SecondaryChipText => HasSourceConflict ? "Conflict" : OrderNotSettled ? "New?" : string.Empty;

    public string? SecondaryChipTooltip => HasSourceConflict
        ? SourceConflictTooltip
        : OrderNotSettled
            ? "Nothing settled whether this version comes before or after what the repo already holds. "
                + "Importing it will ask which."
            : null;

    /// <summary>Stands in for the icon while it loads, and for mods that ship without one.</summary>
    public string Initials { get; }

    [ObservableProperty]
    private ImageSource? _thumbnail;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Whether the row draws the adapter's own lock - "version-sensitive", a fact about the mod. Off on the
    /// editor's right-hand list, whose lock toggle already says what holds the pin and would otherwise sit
    /// a few pixels from a second, identical padlock that means something else.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsAdapterLockIcon))]
    private bool _showAdapterLock = true;

    /// <summary>Whether the row draws the padlock: the mod is version-sensitive, and the page wants it said here.</summary>
    public bool ShowsAdapterLockIcon => IsLocked && ShowAdapterLock;

    /// <summary>Set to false where the list is for browsing rather than picking.</summary>
    [ObservableProperty]
    private bool _isSelectable = true;

    /// <summary>
    /// Whether the checkbox may be clicked. Distinct from <see cref="IsSelectable"/>, which decides
    /// whether there is a checkbox at all: this one is for a list that still shows what is picked
    /// while something is being written from it, where the box has to be visible and inert. See
    /// <c>ProfileModsEditorPageViewModel.IsReadOnly</c>.
    /// </summary>
    [ObservableProperty]
    private bool _isPickable = true;

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
    [NotifyPropertyChangedFor(nameof(IsUpdateRow))]
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

    /// <summary>
    /// What the profile editor's draft has done to this mod, which is a different thing from
    /// <see cref="Status"/>: a status is a fact about the version, and this is something the user
    /// did. It is drawn as its own filled chip and a stripe down the row's edge, so the two cannot be
    /// mistaken for one another. <see cref="ProfileModTouch.None"/> everywhere but the editor.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTouch))]
    [NotifyPropertyChangedFor(nameof(TouchText))]
    private ProfileModTouch _touch;

    /// <summary>What the mark says when hovered: what the saved profile holds, and what the draft would make of it.</summary>
    [ObservableProperty]
    private string? _touchTooltip;

    public bool HasTouch => Touch is not ProfileModTouch.None;

    public string TouchText => Touch switch
    {
        ProfileModTouch.Added => "Added",
        ProfileModTouch.VersionChanged => "Version changed",
        ProfileModTouch.LockChanged => "Lock changed",
        ProfileModTouch.VersionAndLockChanged => "Version & lock changed",
        ProfileModTouch.TakenOut => "Taken out",
        _ => string.Empty
    };

    /// <summary>
    /// Whether the status chip is drawn as an outline rather than a fill. Set where a draft exists to
    /// tell apart from: there a filled chip means "you did this", and the status is only ever a fact
    /// about the version, so it steps back.
    /// </summary>
    [ObservableProperty]
    private bool _outlineStatus;

    /// <summary>
    /// Whether this row's version is newer than what the profile pins - which is what decides where
    /// it sorts, and which of the two pin-moving glyphs it carries. Derived from the status so the
    /// chip and the button cannot disagree.
    /// </summary>
    public bool IsUpdateRow => Status is ModDisplayStatus.UpdateAvailable or ModDisplayStatus.UpdatePending;

    /// <summary>
    /// Whether pressing this row's button moves a pin the profile already has rather than adding a
    /// new one.
    /// </summary>
    /// <remarks>
    /// <b>Not the same question as <see cref="IsUpdateRow"/>, which is what this used to be read off.</b>
    /// The left list's own version selector reaches versions that are older than the pin, and ones
    /// the ordering cannot place against it at all - the <em>New?</em> rows this page exists to
    /// surface - and neither of those is an update. Both still move the pin, so a row deciding its
    /// verb by "is this newer" showed a <b>+</b> labelled <em>Add to this profile</em> over an action
    /// that silently changed an existing pin. Set by the page, which is the thing that knows what the
    /// draft holds; false everywhere else, where a row cannot move anything.
    /// </remarks>
    [ObservableProperty]
    private bool _movesPin;


    /// <summary>
    /// Set by the page that wants the repo's statistics on the row - its size and how many profiles use
    /// it - which is the repo's own list, and nothing else: in the profile editor the question is what
    /// this draft pins, and a count of the repo's other profiles beside it would answer one nobody asked.
    /// </summary>
    [ObservableProperty]
    private bool _showStatistics;

    /// <summary>The registered file's size in the units somebody thinks in. Null where it is not known.</summary>
    public string? SizeText => Mod.SizeBytes is long bytes ? ByteSize.Describe(bytes) : null;

    public bool HasSize => ShowStatistics && SizeText is not null;

    /// <summary>
    /// How many profiles use this version, in the words a row has room for: current ones first, then
    /// how many more reach it only through an older revision. Null for a version the repo does not
    /// hold, which nothing can pin.
    /// </summary>
    public string? UsageText => Mod.Usage is not ModUsage usage
        ? null
        : usage switch
        {
            { IsUnused: true } => "Not used",
            { CurrentProfiles: 0 } => $"Older revisions of {Plural(usage.PastProfiles, "profile")}",
            { PastProfiles: 0 } => Plural(usage.CurrentProfiles, "profile"),
            _ => $"{Plural(usage.CurrentProfiles, "profile")} · {usage.PastProfiles} in older revisions"
        };

    public string? UsageTooltip => Mod.Usage is ModUsage usage
        ? $"Profiles whose newest revision uses this version: {usage.CurrentProfiles}.\n"
            + $"Profiles with an older revision that uses it: {usage.PastProfiles}.\n"
            + "A profile that has used it throughout is in both. A version any revision uses cannot be deleted."
        : null;

    public bool HasUsage => ShowStatistics && UsageText is not null;

    partial void OnShowStatisticsChanged(bool value)
    {
        OnPropertyChanged(nameof(HasUsage));
        OnPropertyChanged(nameof(HasSize));
    }

    private static string Plural(int count, string noun)
        => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
    public bool HasSources => string.IsNullOrWhiteSpace(Sources) is false;

    public bool HasActions => Actions is not null;

    public string StatusText => Status switch
    {
        ModDisplayStatus.New => "New",
        // Two fills, one word: what the row says about this profile is the same either way, and
        // which of the two it is is what the colour carries.
        ModDisplayStatus.UpdateAvailable or ModDisplayStatus.UpdatePending => "Update",
        ModDisplayStatus.NewVersion => "New version",
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
    [NotifyPropertyChangedFor(nameof(HasImportProblem))]
    [NotifyPropertyChangedFor(nameof(ImportProblemTooltip))]
    [NotifyPropertyChangedFor(nameof(IsUploading))]
    private ModImportPhase? _importPhase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportState))]
    [NotifyPropertyChangedFor(nameof(ImportStateText))]
    [NotifyPropertyChangedFor(nameof(ChipText))]
    [NotifyPropertyChangedFor(nameof(HasImportState))]
    [NotifyPropertyChangedFor(nameof(HasImportProblem))]
    [NotifyPropertyChangedFor(nameof(ImportProblemTooltip))]
    private ModImportStatus? _importOutcome;

    /// <summary>Zero to one, and only while uploading - the one phase whose length is knowable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportStateText))]
    [NotifyPropertyChangedFor(nameof(ChipText))]
    private double _importProgress;


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

    /// <summary>
    /// Whether the import left this row unfinished. The row says so with its chip and nothing else -
    /// a warning triangle beside a chip already coloured for failure was the same fact twice, in a
    /// row that has four other things competing for the same twelve pixels.
    /// </summary>
    public bool HasImportProblem => ImportState is ModImportRowState.Failed or ModImportRowState.Skipped;

    /// <summary>
    /// Which mod the dialog was talking about, for whoever comes back to the list after it. On the
    /// chip, since that is what carries the state now - and null where there is no problem, so a row
    /// that is merely new does not sprout a tooltip explaining that nothing went wrong.
    /// </summary>
    public string? ImportProblemTooltip => HasImportProblem
        ? ModImportProblems.DescribeRow(ImportOutcome)
        : null;

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
            ModImportPhase.Storing => "Storing",
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

    }

    /// <summary>
    /// What the import concluded about this row - and, for anything it could not finish, the only
    /// place the whole story is written down.
    /// </summary>
    /// <remarks>
    /// The row gets a mark and the dialog gets a sentence, so an exception - message, inner
    /// exceptions, stack - reaches nobody unless it is logged here. Logged as an error only where
    /// something actually went wrong: a mod the import declined to touch is a decision waiting to be
    /// made, and a mod the repo already held is not news at all.
    /// </remarks>
    public void Apply(ModImportItemResult result)
    {
        ImportOutcome = result.Status;
        ImportPhase = null;

        if (result.IsSuccess)
        {
            return;
        }

        _logger.Log(
            result.Status is ModImportStatus.Failed ? LogLevel.Error : LogLevel.Information,
            result.Exception,
            "Import of {ModId} {VersionId} ended as {Status}: {Message}",
            Mod.ModId.Value, Mod.VersionId.Value, result.Status, result.Message ?? "no message");
    }

    /// <summary>Clears what the last import said, so a second run does not read as the first one.</summary>
    public void ResetImportState()
    {
        ImportPhase = null;
        ImportOutcome = null;
        ImportProgress = 0;
    }

    #endregion


    /// <summary>
    /// Whether this row is one of the ones somebody typing <paramref name="searchTerm"/> meant.
    /// </summary>
    /// <remarks>
    /// <b>The one search in the app.</b> Every filter on every mod list - both lists on the catalog
    /// page and both on the profile editor - runs through here, which is why adopting
    /// <see cref="FuzzySearch"/> was one edit rather than four. The three fields are passed
    /// separately rather than joined, because a term matching across the seam between a name and an
    /// author is a hit nobody can see the reason for.
    /// </remarks>
    public bool Matches(string? searchTerm)
        => FuzzySearch.Matches(searchTerm, Name, Id, Author);

    /// <summary>
    /// Whether two records of one version would draw the same row.
    /// </summary>
    /// <remarks>
    /// Everything this row renders, and nothing else - a differing description is invisible here, a
    /// second occurrence or a newly published image is not. It is what lets a list rebuilt from a
    /// freshly composed catalog keep the rows it already has, which is worth having because building
    /// a new one throws away a loaded thumbnail and the flag saying the user had picked it.
    /// </remarks>
    public static bool RendersTheSame(CatalogModVersion left, CatalogModVersion right)
        => ReferenceEquals(left, right)
        || (left.Identity == right.Identity
            && left.Name == right.Name
            && left.IsOnServer == right.IsOnServer
            && left.IsLocal == right.IsLocal
            && left.Locked == right.Locked
            && left.SequenceNumber == right.SequenceNumber
            && left.FoundIn.Count == right.FoundIn.Count
            && left.ServerImages.Count == right.ServerImages.Count);

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
