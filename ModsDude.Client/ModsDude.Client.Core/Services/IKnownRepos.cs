namespace ModsDude.Client.Core.Services;

/// <summary>
/// Which repos this account is in, as far as the client has been told. Safe to ask from any thread:
/// the drift check asks it off the UI thread.
/// </summary>
public interface IKnownRepos
{
    /// <summary>
    /// Whether the repo is known not to be one of this account's: deleted, archived or left. False
    /// until the repo list has been read.
    /// </summary>
    bool IsGone(Guid repoId);
}
