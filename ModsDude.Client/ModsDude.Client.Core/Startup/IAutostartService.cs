namespace ModsDude.Client.Core.Startup;

public interface IAutostartService
{
    bool IsAvailable { get; }

    AutostartState State { get; }

    /// <summary>The exact command that is registered: quoted, because the path is likely to hold spaces.</summary>
    string Command { get; }

    /// <summary>
    /// Registers the entry, and clears a switch-off in Windows' settings if there was one.
    /// </summary>
    /// <remarks>
    /// Clearing it is right here because this is only ever the user asking - a box they ticked. Windows
    /// keeps the entry disabled otherwise, so ticking it would appear to work and do nothing.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Where this install may not register itself.</exception>
    void Enable();

    void Disable();

    /// <summary>
    /// Repairs an entry that names a path this copy is no longer at, and leaves everything else alone.
    /// </summary>
    /// <remarks>
    /// <b>Only repairs.</b> An absent entry is the user having turned it off, and an entry Windows has
    /// disabled is the user having done so there; neither is put back. What it fixes is the one thing the
    /// user did not do: moving or reinstalling the app, which leaves a startup entry pointing at nothing.
    /// </remarks>
    /// <returns>Whether the entry was rewritten.</returns>
    bool Reconcile();
}
