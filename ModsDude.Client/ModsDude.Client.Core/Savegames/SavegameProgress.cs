namespace ModsDude.Client.Core.Savegames;

/// <summary>Which part of moving a savegame is running, in the order a check-in and a check-out do them.</summary>
public enum SavegameStage
{
    /// <summary>Zipping the slot. The bytes are the slot's own, before compression.</summary>
    Packing,

    /// <summary>Sending the packed archive to storage. The bytes are the archive's.</summary>
    Uploading,

    /// <summary>The server is being told about the new snapshot. No bytes; it is quick, but it is not nothing.</summary>
    Recording,

    /// <summary>Fetching the archive to a temporary file.</summary>
    Downloading,

    /// <summary>Hashing what arrived against the address it was asked for, before it goes anywhere near a slot.</summary>
    Verifying,

    /// <summary>Writing the archive's files into the slot. The bytes are the files', after decompression.</summary>
    Unpacking
}

/// <summary>
/// How far through one stage a savegame operation is.
/// </summary>
/// <param name="Completed">Bytes done in this stage. Restarts at zero with each stage.</param>
/// <param name="Total">
/// What <paramref name="Completed"/> is counting towards, or zero where nothing says - a chunked
/// download, or a stage that has no bytes at all.
/// </param>
public sealed record SavegameProgress(SavegameStage Stage, long Completed, long Total);
