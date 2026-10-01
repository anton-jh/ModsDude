namespace ModsDude.Client.Core.Builds;

/// <summary>
/// Whether the server accepts this build, as its responses say.
/// </summary>
/// <remarks>
/// <para>
/// <b>A server that has moved past this build stays past it.</b> Only restarting into the new build ends
/// that, so an acceptance does not clear it: it can only be a response to a request the old server
/// handled, arriving late.
/// </para>
/// <para>
/// <b>A server that is behind catches up</b>, so the next acceptance clears it.
/// </para>
/// </remarks>
public sealed class ServerCompatibility(BuildNumber client) : IServerCompatibility
{
    private readonly Lock _lock = new();

    private BuildMismatch? _mismatch;


    public event EventHandler? Changed;


    public BuildMismatch? Mismatch
    {
        get
        {
            lock (_lock)
            {
                return _mismatch;
            }
        }
    }


    public void ReportAccepted()
    {
        lock (_lock)
        {
            if (_mismatch is null or { ClientIsBehind: true })
            {
                return;
            }

            _mismatch = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ReportRefused(BuildNumber server)
    {
        var mismatch = new BuildMismatch(client, server);

        lock (_lock)
        {
            if (server == client || mismatch == _mismatch)
            {
                return;
            }

            // A late refusal from a build the server has already moved past.
            if (_mismatch is { ClientIsBehind: true } current && server.Value < current.Server.Value)
            {
                return;
            }

            _mismatch = mismatch;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
