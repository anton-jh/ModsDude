namespace ModsDude.Client.Core.Startup;

/// <summary>Whether this install starts with Windows, as Windows itself would answer it.</summary>
public enum AutostartState
{
    /// <summary>This install is not allowed to: a development build must never register itself.</summary>
    NotAvailable,

    /// <summary>Nothing is registered.</summary>
    Off,

    /// <summary>Registered, and Windows will run it at sign-in.</summary>
    On,

    /// <summary>
    /// Registered, but switched off in Windows' own Startup settings. Windows honours that over the
    /// registration, so the app will not start - and writing the registration again does not change it.
    /// </summary>
    DisabledInWindows
}


/// <summary>
/// The two places Windows keeps a start-at-sign-in entry, reduced to what this needs.
/// </summary>
/// <remarks>
/// A seam rather than the registry itself, because the rules below - what "on" means when Windows has
/// disabled it, and that a stale path is repaired but an absent entry is not resurrected - are the part
/// worth testing, and a test must not write to the developer's own Run key.
/// </remarks>
public interface IStartupRegistry
{
    /// <summary>The command a <c>Run</c> entry launches, or null where there is none.</summary>
    string? GetRunCommand(string name);

    void SetRunCommand(string name, string command);

    void RemoveRunCommand(string name);

    /// <summary>
    /// What Windows' Startup settings recorded for the entry, or null where the user has never touched
    /// it. See <see cref="AutostartService.IsApproved"/> for what the bytes mean.
    /// </summary>
    byte[]? GetApproval(string name);

    void RemoveApproval(string name);
}


/// <summary>
/// Starting with Windows, for one install.
/// </summary>
/// <remarks>
/// <para>
/// <b>The registry is the truth, and this asks it every time.</b> There is no setting mirroring it: the
/// user can switch the entry off in Windows' Settings or Task Manager, and a copy of the answer kept
/// here would go on saying "on" for an app that is not going to start.
/// </para>
/// <para>
/// <b>Only ever for the real install.</b> A debug build registering itself would put a path into
/// <c>bin\Debug</c> in the user's startup list, to be found the first time a rebuild deleted it -
/// so <c>isAvailable</c> is false for anything that is not production and every write refuses.
/// </para>
/// <para>
/// The entry launches the app with <see cref="BackgroundArgument"/>, which is what keeps a start at
/// sign-in from putting a window in front of whatever the user sat down to do.
/// </para>
/// </remarks>
/// <param name="name">The value name the entry is filed under, per install.</param>
/// <param name="executablePath">The running executable. Null, or the .NET host, means there is nothing stable to register.</param>
public sealed class AutostartService(
    IStartupRegistry registry,
    string name,
    string? executablePath,
    bool isAvailable)
{
    /// <summary>What a start at sign-in passes, so the app comes up in the tray and not on screen.</summary>
    public const string BackgroundArgument = "--background";


    public bool IsAvailable => isAvailable && HasStableExecutable;

    private bool HasStableExecutable => executablePath is { Length: > 0 }
        && string.Equals(Path.GetFileNameWithoutExtension(executablePath), "dotnet", StringComparison.OrdinalIgnoreCase) is false;


    public AutostartState State
    {
        get
        {
            if (IsAvailable is false)
            {
                return AutostartState.NotAvailable;
            }

            if (registry.GetRunCommand(name) is null)
            {
                return AutostartState.Off;
            }

            return IsApproved(registry.GetApproval(name))
                ? AutostartState.On
                : AutostartState.DisabledInWindows;
        }
    }

    /// <summary>The exact command that is registered: quoted, because the path is likely to hold spaces.</summary>
    public string Command => $"\"{executablePath}\" {BackgroundArgument}";


    /// <summary>
    /// Registers the entry, and clears a switch-off in Windows' settings if there was one.
    /// </summary>
    /// <remarks>
    /// Clearing it is right here because this is only ever the user asking - a box they ticked. Windows
    /// keeps the entry disabled otherwise, so ticking it would appear to work and do nothing.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Where this install may not register itself.</exception>
    public void Enable()
    {
        Require();

        registry.SetRunCommand(name, Command);
        registry.RemoveApproval(name);
    }

    public void Disable()
    {
        Require();

        registry.RemoveRunCommand(name);
    }

    /// <summary>
    /// Repairs an entry that names a path this copy is no longer at, and leaves everything else alone.
    /// </summary>
    /// <remarks>
    /// <b>Only repairs.</b> An absent entry is the user having turned it off, and an entry Windows has
    /// disabled is the user having done so there; neither is put back. What it fixes is the one thing the
    /// user did not do: moving or reinstalling the app, which leaves a startup entry pointing at nothing.
    /// </remarks>
    /// <returns>Whether the entry was rewritten.</returns>
    public bool Reconcile()
    {
        if (IsAvailable is false)
        {
            return false;
        }

        var current = registry.GetRunCommand(name);

        if (current is null || string.Equals(current, Command, StringComparison.Ordinal))
        {
            return false;
        }

        registry.SetRunCommand(name, Command);

        return true;
    }


    /// <summary>
    /// Whether Windows' Startup settings allow the entry to run.
    /// </summary>
    /// <remarks>
    /// The value is twelve bytes, and only the first says anything: an <b>even</b> number (2, 6) is
    /// enabled and an <b>odd</b> one (3, 7) is disabled, the rest being the time it was switched off.
    /// A missing value means the user has never touched it, which is enabled.
    /// </remarks>
    public static bool IsApproved(byte[]? approval)
        => approval is not { Length: > 0 } || (approval[0] & 1) == 0;


    private void Require()
    {
        if (IsAvailable is false)
        {
            throw new InvalidOperationException("This install cannot start with Windows.");
        }
    }
}
