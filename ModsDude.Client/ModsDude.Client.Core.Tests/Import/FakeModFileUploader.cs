using ModsDude.Client.Core.Import;

namespace ModsDude.Client.Core.Tests.Import;

/// <summary>
/// Hashes what it is handed and tells the fake server the blob now exists - which is the only way a
/// blob comes into being there, so the never-register-before-upload invariant is really being
/// observed rather than assumed.
/// </summary>
internal sealed class FakeModFileUploader(FakeModsDudeServer server) : IModFileUploader
{
    private readonly Lock _lock = new();

    private int _inFlight;


    /// <summary>The most mods that were uploading at once, for asserting the concurrency bound.</summary>
    public int PeakConcurrency { get; private set; }

    /// <summary>Runs inside the upload, where a test can cancel or throw partway through a batch.</summary>
    public Func<string, Task>? OnUpload { get; set; }


    public async Task<string> UploadAsync(ModFileUpload upload, CancellationToken cancellationToken)
    {
        Enter();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (OnUpload is not null)
            {
                await OnUpload(upload.Link);
            }

            // Long enough that concurrent mods actually overlap rather than each finishing before
            // the next is scheduled.
            await Task.Delay(5, cancellationToken);

            using var content = upload.OpenContent();
            var hash = await ModContentHasher.ComputeAsync(content, cancellationToken);

            upload.BytesTransferred?.Report(content.Length);

            Assert.Equal(FakeModsDudeServer.ContentHashMetadataKey, upload.ContentHashMetadataKey);

            server.CompleteUpload(upload.Link, hash);

            return hash;
        }
        finally
        {
            Leave();
        }
    }


    private void Enter()
    {
        lock (_lock)
        {
            _inFlight++;
            PeakConcurrency = Math.Max(PeakConcurrency, _inFlight);
        }
    }

    private void Leave()
    {
        lock (_lock)
        {
            _inFlight--;
        }
    }
}
