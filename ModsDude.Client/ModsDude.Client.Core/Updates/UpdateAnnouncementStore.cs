namespace ModsDude.Client.Core.Updates;

/// <summary>
/// Whether the server has said that an update is on its way, as of the last change poll.
/// </summary>
public interface IUpdateAnnouncementStore
{
    /// <summary>When the update was announced, in UTC. Null where none is on its way.</summary>
    DateTime? AnnouncedAt { get; }

    /// <summary>Raised, from any thread, when <see cref="AnnouncedAt"/> changed.</summary>
    event EventHandler? Changed;

    void Set(DateTime? announcedAt);
}


public sealed class UpdateAnnouncementStore : IUpdateAnnouncementStore
{
    private readonly Lock _lock = new();
    private DateTime? _announcedAt;


    public event EventHandler? Changed;


    public DateTime? AnnouncedAt
    {
        get
        {
            lock (_lock)
            {
                return _announcedAt;
            }
        }
    }

    public void Set(DateTime? announcedAt)
    {
        lock (_lock)
        {
            if (_announcedAt == announcedAt)
            {
                return;
            }

            _announcedAt = announcedAt;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
