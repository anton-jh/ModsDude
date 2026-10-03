using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Statistics;

/// <summary>
/// A link issued to move one file to or from storage. Clients use the link directly against storage,
/// so this records what was offered, not what arrived.
/// </summary>
public class FileTransfer
{
    // ef
    private FileTransfer() { }

    public FileTransfer(
        RepoId repoId,
        UserId userId,
        TransferredFile file,
        TransferDirection direction,
        long sizeBytes,
        DateTime at)
    {
        if (sizeBytes < 0)
        {
            throw new DomainValidationException("A transferred file cannot have a negative size.");
        }

        RepoId = repoId;
        UserId = userId;
        File = file;
        Direction = direction;
        SizeBytes = sizeBytes;
        At = at;
    }


    public FileTransferId Id { get; init; } = new(Guid.NewGuid());

    public RepoId RepoId { get; private set; }
    public UserId UserId { get; private set; }
    public TransferredFile File { get; private set; }
    public TransferDirection Direction { get; private set; }
    public long SizeBytes { get; private set; }
    public DateTime At { get; private set; }
}


public readonly record struct FileTransferId(Guid Value);

public enum TransferredFile
{
    Mod,
    Savegame
}

public enum TransferDirection
{
    Upload,
    Download
}
