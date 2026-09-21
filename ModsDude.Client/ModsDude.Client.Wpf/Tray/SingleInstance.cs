using ModsDude.Client.Core;
using System.Runtime.InteropServices;

namespace ModsDude.Client.Wpf.Tray;

/// <summary>
/// The one running copy of the app, and the way a second launch reaches it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not optional once there is a tray.</b> An app that survives its window being closed is an app
/// the user will launch again from the Start menu without noticing the first is still there, and two
/// copies would be two drift monitors, two watchers and two writers of the same <c>state.json</c>.
/// </para>
/// <para>
/// A named mutex says who is first; a named event is how everyone after that says "come to the
/// front". The event rather than a pipe or a window message because it carries no payload and needs
/// no window to exist yet - which is what a copy started hidden at logon does not have. The names are
/// <c>Local\</c>, so two users on one machine each get their own app rather than fighting for it.
/// </para>
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    // Per install, not per machine: a debug build must be able to run beside the copy somebody is
    // actually using rather than being turned into a request to bring that one forward.
    private static string MutexName => $@"Local\{AppIdentity.Name}.Client.Instance";
    private static string ActivateName => $@"Local\{AppIdentity.Name}.Client.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly ManualResetEventSlim _stopped = new();
    private Thread? _listener;


    private SingleInstance(Mutex mutex, EventWaitHandle activate)
    {
        _mutex = mutex;
        _activate = activate;
    }


    /// <summary>
    /// Claims the instance, or asks whoever holds it to come forward.
    /// </summary>
    /// <param name="bringExistingForward">False for a start the app made itself, which must not surface a window.</param>
    /// <returns>Null where another copy is running - it has been told, and this one should exit.</returns>
    public static SingleInstance? TryAcquire(bool bringExistingForward = true)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);

        if (created is false)
        {
            // This process was just started by the user, so it is allowed to hand the foreground to
            // the copy it is about to ask to take it. Without this, Windows answers the running copy's
            // request for focus with a flashing taskbar button - which, for a window that was hidden
            // to the tray, is no button at all.
            AllowSetForegroundWindow(uint.MaxValue);

            // A start at sign-in finding the app already running has no business raising its window:
            // nobody asked for it. Only a launch the user made is a request to come forward.
            if (bringExistingForward)
            {
                activate.Set();
            }
            activate.Dispose();
            mutex.Dispose();

            return null;
        }

        return new SingleInstance(mutex, activate);
    }

    /// <summary>
    /// Calls <paramref name="onActivated"/> every time a later launch asks for this copy to come
    /// forward. On a thread of its own - marshal to the UI thread inside the callback.
    /// </summary>
    public void ListenForActivation(Action onActivated)
    {
        _listener = new Thread(() =>
        {
            WaitHandle[] handles = [_activate, _stopped.WaitHandle];

            while (WaitHandle.WaitAny(handles) == 0)
            {
                onActivated();
            }
        })
        {
            IsBackground = true,
            Name = "ModsDude single-instance listener"
        };

        _listener.Start();
    }

    public void Dispose()
    {
        _stopped.Set();
        _listener?.Join(TimeSpan.FromMilliseconds(500));

        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _activate.Dispose();
        _stopped.Dispose();
    }


    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
