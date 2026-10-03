namespace ModsDude.Client.Core.Accounts;

/// <summary>
/// Whether the server accepts the signed-in account. Any accepted request clears a block, which is
/// how an unblock, or switching to another account, is noticed.
/// </summary>
public sealed class AccountStatus : IAccountStatus
{
    private int _isBlocked;


    public event EventHandler? Changed;


    public bool IsBlocked => Volatile.Read(ref _isBlocked) == 1;


    public void ReportAccepted() => Set(false);

    public void ReportBlocked() => Set(true);


    private void Set(bool blocked)
    {
        var value = blocked ? 1 : 0;

        if (Interlocked.Exchange(ref _isBlocked, value) != value)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
