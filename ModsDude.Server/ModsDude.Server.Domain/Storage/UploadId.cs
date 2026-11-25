namespace ModsDude.Server.Domain.Storage;

/// <summary>One upload's own blob, minted by the server with the first link for it and named again to renew that link.</summary>
public readonly record struct UploadId(Guid Value);
