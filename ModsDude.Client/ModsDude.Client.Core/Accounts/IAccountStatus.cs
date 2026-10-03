namespace ModsDude.Client.Core.Accounts;

public interface IAccountStatus
{
    /// <summary>Whether the server refuses the signed-in account, as its last answer said.</summary>
    bool IsBlocked { get; }

    /// <summary>Raised, from any thread, when <see cref="IsBlocked"/> changed.</summary>
    event EventHandler? Changed;

    void ReportAccepted();

    void ReportBlocked();
}
