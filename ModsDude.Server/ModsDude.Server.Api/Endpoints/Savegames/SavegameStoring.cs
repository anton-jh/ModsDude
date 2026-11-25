using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Persistence.Storage;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>What a check-in or publish answers when its save could not be stored at its address.</summary>
internal static class SavegameStoring
{
    /// <returns>The problem to answer with, or null where the save is stored.</returns>
    public static CustomProblemDetails? Refusal(StagedUploadResult storing, RepoId repoId, SavegameId savegameId, string contentHash, long sizeBytes)
    {
        return storing.Outcome switch
        {
            StagedUploadOutcome.Stored => null,
            StagedUploadOutcome.Missing => Problems.SavegameFileDoesNotExist(repoId, savegameId, contentHash),
            StagedUploadOutcome.UploadInvalid => Problems.SavegameUploadInvalid(sizeBytes, storing.Blob!.Length, storing.Blob.RecordedContentHash),
            StagedUploadOutcome.UploadChanged => Problems.UploadChanged,
            _ => throw new ArgumentOutOfRangeException(nameof(storing), storing.Outcome, null)
        };
    }
}
