namespace ModsDude.Client.Core.Savegames;

public interface ILockedPinDrift
{
    /// <summary>
    /// Whether any locked pin moved between two revisions of a profile. An unlocked mod at another
    /// version is untidy; a locked one can damage a save. False where the server could not be asked.
    /// </summary>
    Task<bool> HasMovedAsync(Guid repoId, Guid profileId, int from, int to, CancellationToken cancellationToken);
}
