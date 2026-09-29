using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// A remote source as the profile mod editor holds it: whether its chip is on, what it has been asked,
/// and what it answered. Answers are kept across chip toggles, so only mods nobody has asked about yet
/// are looked up.
/// </summary>
public sealed class RemoteModSourceState(IRemoteModSource remote)
{
    public IRemoteModSource Remote { get; } = remote;
    public ModSourceId Id { get; } = ModSourceId.ForRemote(remote.Key);

    public ModSource Source => new(Id, Remote.DisplayName, Describe(), ModSourceKind.Remote);

    /// <summary>On from the start: it reads no disk, and a chip nobody knows to click is updates nobody sees.</summary>
    public bool IsEnabled { get; set; } = true;

    public bool IsLookingUp { get; set; }
    public string? Error { get; set; }

    public HashSet<ModKey> Asked { get; } = [];
    public Dictionary<ModKey, RemoteModOffer> Answers { get; } = [];

    public bool HasAnswered { get; set; }
    public DateTimeOffset? CurrentAsOf { get; set; }

    /// <summary>The source answered but could not vouch for the answer, so it may be missing most of what it has.</summary>
    public bool IsIncomplete => HasAnswered && CurrentAsOf is null;


    public void Forget()
    {
        Asked.Clear();
        Answers.Clear();
        HasAnswered = false;
        CurrentAsOf = null;
        Error = null;
    }


    private string Describe()
    {
        var name = Remote.DisplayName;
        var what = $"Newer versions {name} has of the mods here, as a link on each mod's row.";

        if (IsEnabled is false)
        {
            return $"{what}\nSwitched off: no links are shown.";
        }

        if (Error is not null)
        {
            return what;
        }

        if (HasAnswered is false)
        {
            return IsLookingUp ? $"{what}\nLooking up…" : what;
        }

        return CurrentAsOf is DateTimeOffset asOf
            ? $"{what}\nAs of {asOf.LocalDateTime:g}."
            : $"{what}\n{name} could not say how current this is, so it may be missing most of what it has. Switch this off and on again later to ask again.";
    }
}
