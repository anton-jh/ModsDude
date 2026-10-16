using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.RemoteSources;
using ModsDude.Client.Wpf.Shell.Modals;

namespace ModsDude.Client.Wpf.Profiles.Editor;

/// <summary>
/// One remote source on the profile editor: what it has been asked, what it answered, and whether
/// asking failed. Each mod is asked about once; a new answer replaces the last one per mod, and every
/// version it has answered with is kept so a pin made on one keeps resolving.
/// </summary>
public sealed class RemoteSourceLookup(IRemoteModSource source, IErrorReporter errorReporter, CancellationToken cancellation)
{
    private readonly HashSet<ModKey> _asked = [];
    private readonly Dictionary<ModKey, RemoteModVersion> _current = [];
    private readonly Dictionary<ModVersionIdentity, RemoteModVersion> _retained = [];
    private IReadOnlyCollection<ModKey> _known = [];


    /// <summary>Raised on the UI thread when an answer arrives, or a lookup starts or fails.</summary>
    public event Action? Changed;


    public IRemoteModSource Source => source;

    public ModSourceId Id { get; } = ModSourceId.ForRemote(source.Key);

    public ModSource AsSource { get; } = new(ModSourceId.ForRemote(source.Key), source.DisplayName, string.Empty, ModSourceKind.Remote);

    public bool IsLookingUp { get; private set; }

    /// <summary>Why the last lookup failed, in a few words, until it is asked again.</summary>
    public string? Error { get; private set; }

    public bool HasAnswered { get; private set; }

    public DateTimeOffset? CurrentAsOf { get; private set; }

    public string Status => (IsLookingUp, HasAnswered, CurrentAsOf) switch
    {
        (true, _, _) => "Looking up…",
        (_, false, _) => "Not looked up yet.",
        (_, _, DateTimeOffset asOf) => $"As of {asOf.LocalDateTime:g}.",
        _ => "May be missing most of what it has."
    };


    public RemoteSourceAnswers Answers(bool isEnabled) => new(source, isEnabled, [.. _current.Values], [.. _retained.Values]);

    /// <summary>Asks about whichever of <paramref name="mods"/> it has not been asked about.</summary>
    public void LookUp(IReadOnlyCollection<ModKey> mods)
    {
        _known = mods;

        if (IsLookingUp || Error is not null)
        {
            return;
        }

        var unasked = mods.Where(x => _asked.Contains(x) is false).ToList();

        if (unasked.Count > 0)
        {
            // Not awaited: the answer arrives through Changed, and every failure is caught and
            // recorded inside.
            _ = LookUpAsync(unasked);
        }
    }

    /// <summary>Asks about everything again. What it answered before stays until the new answer replaces it.</summary>
    public void CheckAgain()
    {
        if (IsLookingUp)
        {
            return;
        }

        _asked.Clear();
        Error = null;

        LookUp(_known);
    }


    private async Task LookUpAsync(List<ModKey> mods)
    {
        _asked.UnionWith(mods);
        IsLookingUp = true;

        Changed?.Invoke();

        try
        {
            var lookup = await source.LookUpAsync(mods, cancellation);

            foreach (var version in lookup.Versions)
            {
                _current[version.ModId] = version;
                _retained.TryAdd(version.Identity, version);
            }

            HasAnswered = true;
            CurrentAsOf = lookup.CurrentAsOf;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            _asked.ExceptWith(mods);
            Error = "Could not be reached.";

            errorReporter.Record(exception, $"looking up mods on {source.DisplayName}");
        }
        finally
        {
            IsLookingUp = false;
        }

        Changed?.Invoke();
    }
}
