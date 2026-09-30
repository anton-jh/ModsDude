using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles.Editor;
using ModsDude.Client.Wpf.Shell.Modals;
using System.Diagnostics;

namespace ModsDude.Client.Wpf.Profiles.Editor;

/// <summary>
/// Newer versions of the mods here that the game's remote update providers know of - ModHub, for
/// Farming Simulator. Each mod is looked up once; answers are kept until a provider is checked again.
/// </summary>
public sealed class RemoteUpdatesViewModel(
    IEnumerable<IRemoteUpdateProvider> providers,
    IErrorReporter errorReporter,
    CancellationToken cancellation)
{
    public IReadOnlyList<RemoteUpdateProviderViewModel> Providers { get; } =
        [.. providers.Select(x => new RemoteUpdateProviderViewModel(x, cancellation))];

    public bool HasProviders => Providers.Count > 0;

    /// <summary>The name updates are credited to where they are counted together.</summary>
    public string Name => Providers.Count == 1 ? Providers[0].Name : "online";


    /// <summary>Raised on the UI thread when a provider answers, fails, or is being checked again.</summary>
    public event Action? Changed
    {
        add
        {
            foreach (var provider in Providers)
            {
                provider.Changed += value;
            }
        }
        remove
        {
            foreach (var provider in Providers)
            {
                provider.Changed -= value;
            }
        }
    }

    /// <summary>Raised after a download page has been opened: the file will land in Downloads.</summary>
    public event Action? DownloadsWanted;


    public IReadOnlyList<RemoteUpdateAnswers> Answers() => [.. Providers.Select(x => x.Answers())];

    /// <summary>Asks every provider about whichever of <paramref name="mods"/> it has not been asked about.</summary>
    public void LookUp(IEnumerable<ModKey> mods)
    {
        var known = mods.ToList();

        foreach (var provider in Providers)
        {
            provider.LookUp(known);
        }
    }

    public RemoteUpdateViewModel? Show(RemoteUpdate? update)
        => update is null ? null : new RemoteUpdateViewModel(update.Version, update.ProviderName, Open);


    /// <summary>Opens the page a newer version is downloaded from. Only ever a web page: the address came from the server.</summary>
    private void Open(RemoteModVersion version)
    {
        if (Uri.TryCreate(version.PageUrl, UriKind.Absolute, out var uri) is false
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _ = errorReporter.ShowAsync(exception, "opening the mod's page");

            return;
        }

        DownloadsWanted?.Invoke();
    }
}


/// <summary>One provider: what it has been asked, what it answered, and a line saying so in the updates band.</summary>
public sealed partial class RemoteUpdateProviderViewModel(IRemoteUpdateProvider provider, CancellationToken cancellation) : ObservableObject
{
    private readonly HashSet<ModKey> _asked = [];
    private readonly Dictionary<ModKey, RemoteModVersion> _answers = [];
    private IReadOnlyCollection<ModKey> _known = [];


    public event Action? Changed;


    public string Name => provider.DisplayName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(CheckAgainCommand))]
    private bool _isLookingUp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(HasFailed))]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _hasAnswered;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private DateTimeOffset? _currentAsOf;

    public bool HasFailed => Error is not null;

    public string StatusText => (IsLookingUp, Error, HasAnswered, CurrentAsOf) switch
    {
        (true, _, _, _) => $"{Name}: looking up…",
        (_, string error, _, _) => error,
        (_, _, false, _) => $"{Name}: not looked up yet",
        (_, _, _, DateTimeOffset asOf) => $"{Name}: as of {asOf.LocalDateTime:g}",
        _ => $"{Name} could not say how current this is, so it may be missing most of what it has."
    };


    public RemoteUpdateAnswers Answers() => new(Name, _answers.Values);

    public void LookUp(IReadOnlyCollection<ModKey> mods)
    {
        _known = mods;

        if (IsLookingUp || HasFailed)
        {
            return;
        }

        var unasked = mods.Where(x => _asked.Contains(x) is false).ToList();

        if (unasked.Count > 0)
        {
            _ = LookUpAsync(unasked);
        }
    }


    /// <summary>Forgets every answer and asks about everything again.</summary>
    [RelayCommand(CanExecute = nameof(CanCheckAgain))]
    private void CheckAgain()
    {
        _asked.Clear();
        _answers.Clear();
        HasAnswered = false;
        CurrentAsOf = null;
        Error = null;

        Changed?.Invoke();

        LookUp(_known);
    }

    private bool CanCheckAgain() => IsLookingUp is false;

    private async Task LookUpAsync(List<ModKey> mods)
    {
        _asked.UnionWith(mods);
        IsLookingUp = true;

        try
        {
            var lookup = await provider.LookUpAsync(mods, cancellation);

            foreach (var version in lookup.Versions)
            {
                _answers[version.ModId] = version;
            }

            HasAnswered = true;
            CurrentAsOf = lookup.CurrentAsOf;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            _asked.ExceptWith(mods);
            Error = exception is ApiException api
                ? $"{Name} could not be looked up ({api.StatusCode})."
                : $"{Name} could not be looked up.";
        }
        finally
        {
            IsLookingUp = false;
        }

        Changed?.Invoke();
    }
}
