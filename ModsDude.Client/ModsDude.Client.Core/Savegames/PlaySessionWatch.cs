using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Notices;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using System.Diagnostics;

namespace ModsDude.Client.Core.Savegames;

/// <summary>Whether any of a game's processes is alive on this machine.</summary>
/// <remarks>An interface so <see cref="PlaySessionWatch"/> can be exercised without starting a game.</remarks>
public interface IGameProcesses
{
    bool IsAnyRunning(IReadOnlyList<string> processNames);
}

/// <summary><see cref="IGameProcesses"/> over the machine's own process list.</summary>
public sealed class SystemGameProcesses : IGameProcesses
{
    public bool IsAnyRunning(IReadOnlyList<string> processNames)
    {
        foreach (var name in processNames)
        {
            var found = Process.GetProcessesByName(name);

            foreach (var process in found)
            {
                process.Dispose();
            }

            if (found.Length > 0)
            {
                return true;
            }
        }

        return false;
    }
}


/// <summary>Which processes are a game running, for the games this client can name an adapter for.</summary>
/// <remarks>
/// A seam for the reason <see cref="ILocalSavegameAdapters"/> is one: the adapter hydrates from a
/// repo's base settings, which a game does not carry, so the answer goes through whichever repo on
/// this machine serves the game's scope.
/// </remarks>
public interface IGameProcessNames
{
    /// <returns>Empty where no loaded repo serves the game, or its adapter cannot name a process.</returns>
    IReadOnlyList<string> Get(GameIdentity game);
}

/// <summary><see cref="IGameProcessNames"/> over the repos this client has loaded.</summary>
public sealed class RepoGameProcessNames(RepoRepository repos) : IGameProcessNames
{
    public IReadOnlyList<string> Get(GameIdentity game)
        => repos.Repos.FirstOrDefault(x => x.Scope == game)?.Adapter.ProcessNames ?? [];
}


/// <summary>A checked-out savegame that was played in a session that has just ended.</summary>
/// <param name="SlotDisplayName">What the game calls the save, where the slot could be read.</param>
public sealed record PlayedSavegame(
    GameIdentity Game,
    string GameName,
    Guid RepoId,
    Guid SavegameId,
    string? SlotDisplayName)
{
    /// <summary>The notice the drift check raises about this savegame, which is where check-in starts.</summary>
    public string NoticeKey => NoticeBuilder.SavegameKey(SavegameId);
}


/// <summary>
/// Notices a game being closed after somebody played a savegame this machine has checked out, which
/// is the moment a reminder to check it in is worth the most.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only while something is checked out.</b> A game holding nothing is not looked for at all, so
/// the ordinary machine - which holds nothing, nearly all of the time - asks for no process list.
/// </para>
/// <para>
/// <b>Played this session, and not yet checked in.</b> Each held slot is hashed when the game is
/// first seen running and again when it is gone; a save counts when its bytes differ from both the
/// start of the session and the snapshot the check-out holds. The first half keeps an evening spent
/// in another save from reminding about this one; the second keeps a <em>keep playing</em> check-in
/// halfway through from being reminded about when nothing has been played since.
/// </para>
/// <para>
/// <b>A game already running when the app started</b> has no start to compare with, and neither does
/// a save checked out partway through a session. Both are measured from the check-out instead, which
/// is the best guess available: whatever moved since then moved on this disk and nowhere else.
/// </para>
/// </remarks>
public sealed class PlaySessionWatch(
    IDriftCandidateSource games,
    SavegameBindingStore bindings,
    IHeldSavegames held,
    IGameProcessNames processNames,
    IGameProcesses processes,
    ILogger<PlaySessionWatch> logger)
{
    private readonly Lock _lock = new();

    /// <summary>The games seen running, each with its held slots' hashes from when it was first seen.</summary>
    private readonly Dictionary<GameIdentity, IReadOnlyDictionary<Guid, string>> _sessions = [];

    private bool _polledBefore;


    /// <summary>
    /// Raised by <see cref="PollAsync"/>, on the thread that called it, for a session that ended with
    /// at least one checked-out savegame played in it.
    /// </summary>
    public event EventHandler<IReadOnlyList<PlayedSavegame>>? Played;


    /// <summary>Whether the game was running at the last poll.</summary>
    public bool IsRunning(GameIdentity game)
    {
        lock (_lock)
        {
            return _sessions.ContainsKey(game);
        }
    }

    /// <summary>
    /// Looks at which games are running now, and reports the checked-out savegames played in every
    /// session that has ended since the last look.
    /// </summary>
    public async Task<IReadOnlyList<PlayedSavegame>> PollAsync(CancellationToken ct)
    {
        var played = new List<PlayedSavegame>();
        var first = _polledBefore is false;

        _polledBefore = true;

        foreach (var candidate in games.GetDriftCandidates())
        {
            var game = candidate.Identity;
            IReadOnlyDictionary<Guid, string>? session;

            lock (_lock)
            {
                session = _sessions.GetValueOrDefault(game);
            }

            // Asked only while there is something to remind about, or a session to see the end of.
            if (session is null && bindings.GetBindings(game).Count == 0)
            {
                continue;
            }

            var names = processNames.Get(game);
            var running = names.Count > 0 && processes.IsAnyRunning(names);

            if (running && session is null)
            {
                // Nothing to measure from where the game was already up before anybody was looking.
                var baseline = first ? new Dictionary<Guid, string>() : await HashHeldAsync(game, ct);

                lock (_lock)
                {
                    _sessions[game] = baseline;
                }

                logger.LogInformation("{Game} is running with a savegame checked out.", candidate.Name);
            }
            else if (running is false && session is not null)
            {
                // Read before the session is let go of, so nobody asking IsRunning in the meantime is
                // told the game is closed before the answer about what was played in it exists.
                var inSession = await PlayedInAsync(candidate, session, ct);

                played.AddRange(inSession);

                lock (_lock)
                {
                    _sessions.Remove(game);
                }

                logger.LogInformation("{Game} has closed; {Count} checked-out savegames were played.", candidate.Name, inSession.Count);
            }
        }

        if (played.Count > 0)
        {
            Played?.Invoke(this, played);
        }

        return played;
    }


    private async Task<IReadOnlyDictionary<Guid, string>> HashHeldAsync(GameIdentity game, CancellationToken ct)
    {
        var baseline = new Dictionary<Guid, string>();

        foreach (var reading in await ReadOrNothingAsync(game, ct))
        {
            if (reading.CurrentHash is string hash)
            {
                baseline[reading.Binding.SavegameId] = hash;
            }
        }

        return baseline;
    }

    private async Task<IReadOnlyList<PlayedSavegame>> PlayedInAsync(
        DriftCandidate game, IReadOnlyDictionary<Guid, string> session, CancellationToken ct)
    {
        var played = new List<PlayedSavegame>();

        foreach (var (binding, current, name) in await ReadOrNothingAsync(game.Identity, ct))
        {
            // An empty or unreadable slot says nothing, the way it does to the drift check.
            if (current is null || ModContentHasher.Matches(current, binding.ContentHash))
            {
                continue;
            }

            if (session.TryGetValue(binding.SavegameId, out var before) && ModContentHasher.Matches(current, before))
            {
                continue;
            }

            played.Add(new PlayedSavegame(game.Identity, game.Name, binding.RepoId, binding.SavegameId, name));
        }

        return played;
    }

    /// <summary>
    /// The held slots, or none where reading them failed - a reminder is a courtesy, and a missing
    /// folder is no reason for the poll to throw.
    /// </summary>
    private async Task<IReadOnlyList<HeldSlotReading>> ReadOrNothingAsync(GameIdentity game, CancellationToken ct)
    {
        try
        {
            return await held.ReadHeldAsync(game, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not read the savegames {Game} has checked out.", game);

            return [];
        }
    }
}
