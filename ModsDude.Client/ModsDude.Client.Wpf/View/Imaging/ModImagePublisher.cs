using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.IO;

namespace ModsDude.Client.Wpf.View.Imaging;

/// <inheritdoc cref="IModImagePublisher"/>
public class ModImagePublisher(
    IImagesClient imagesClient,
    IModsClient modsClient,
    IModImageStore store)
    : IModImagePublisher, IModImageBackfill
{
    /// <summary>The server refuses a larger batch, and each hash there costs it a blob round trip.</summary>
    private const int _existenceCheckBatchSize = 1000;

    /// <summary>
    /// An import runs several mods at once and a mod ships around five images, so without a bound
    /// the decodes alone would saturate the machine the user is still using.
    /// </summary>
    private readonly SemaphoreSlim _throttle = new(Math.Max(2, Environment.ProcessorCount / 2));


    public Task PublishAsync(Guid repoId, ModKey modId, ModVersionKey versionId, LocalMod mod, CancellationToken cancellationToken)
    {
        return BackfillAsync(repoId, modId, versionId, mod, cancellationToken);
    }

    public async Task<IReadOnlyList<ModImageReference>> BackfillAsync(
        Guid repoId, ModKey modId, ModVersionKey versionId, LocalMod mod, CancellationToken cancellationToken)
    {
        try
        {
            var generated = await GenerateAsync(mod, cancellationToken);

            if (generated.Count == 0)
            {
                return [];
            }

            var stored = await StoreAsync(generated, cancellationToken);

            // Only what actually reached the server is referenced. A reference to an address nothing
            // was uploaded to is a permanently broken image, and the endpoint rejects the whole set
            // over one of them.
            var references = generated
                .SelectMany(image => image.Renditions
                    .Where(rendition => stored.Contains(rendition.Hash))
                    .Select(rendition => new ModImageReference(
                        rendition.Hash, image.Kind, rendition.Rendition, image.Index, image.FileName)))
                .ToList();

            if (references.Count == 0)
            {
                return [];
            }

            await modsClient.SetModVersionImagesV1Async(
                repoId, modId.Value, versionId.Value,
                new SetModVersionImagesRequest() { Images = [.. references.Select(x => x.ToDto())] },
                cancellationToken);

            // The client that derived these is usually the one about to draw them, and it already
            // holds the bytes.
            foreach (var rendition in generated.SelectMany(x => x.Renditions))
            {
                await store.PutAsync(rendition.Hash, rendition.Bytes, cancellationToken);
            }

            return references;
        }
        catch (Exception)
        {
            // Imagery is decoration. An import of 2,000 mods must not fail - or worse, half-fail -
            // because a thumbnail upload timed out, and the version renders with initials until
            // somebody holding the file looks at it again.
            return [];
        }
    }


    private async Task<IReadOnlyList<GeneratedModImage>> GenerateAsync(LocalMod mod, CancellationToken cancellationToken)
    {
        var sources = new List<(ModImage Image, ModImageKind Kind, int Index)>();

        if (mod.Icon is ModImage icon)
        {
            sources.Add((icon, ModImageKind.Icon, 0));
        }

        sources.AddRange(mod.Images.Select((x, i) => (x, ModImageKind.StoreImage, i)));

        var generated = await Task.WhenAll(sources.Select(x => TryGenerateAsync(x.Image, x.Kind, x.Index, cancellationToken)));

        return [.. generated.OfType<GeneratedModImage>()];
    }

    private async Task<GeneratedModImage?> TryGenerateAsync(ModImage image, ModImageKind kind, int index, CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken);

        try
        {
            return await ModImageDerivativeGenerator.GenerateAsync(image, kind, index, cancellationToken);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested is false)
        {
            // One unreadable image costs the mod that image, not its whole gallery.
            return null;
        }
        finally
        {
            _throttle.Release();
        }
    }

    /// <summary>The addresses the server holds once this returns, whether it already did or not.</summary>
    private async Task<HashSet<string>> StoreAsync(IReadOnlyList<GeneratedModImage> generated, CancellationToken cancellationToken)
    {
        var renditions = generated
            .SelectMany(x => x.Renditions)
            .DistinctBy(x => x.Hash)
            .ToList();

        // Asked before anything is uploaded: after the first import into a repo almost every image
        // is already there - releases of one mod reuse their artwork, and so do mods across repos -
        // so 2,000 mods at ~20 images each is tens of thousands of uploads that need not happen.
        var present = await CheckWhichExistAsync([.. renditions.Select(x => x.Hash)], cancellationToken);

        var uploads = renditions
            .Where(x => present.Contains(x.Hash) is false)
            .Select(x => TryUploadAsync(x, cancellationToken));

        foreach (var uploaded in await Task.WhenAll(uploads))
        {
            if (uploaded is string hash)
            {
                present.Add(hash);
            }
        }

        return present;
    }

    private async Task<HashSet<string>> CheckWhichExistAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken)
    {
        var present = new HashSet<string>();

        foreach (var batch in hashes.Chunk(_existenceCheckBatchSize))
        {
            var response = await imagesClient.CheckImagesExistV1Async(
                new CheckImagesExistRequest() { Hashes = batch },
                cancellationToken);

            present.UnionWith(response.Present);
        }

        return present;
    }

    private async Task<string?> TryUploadAsync(GeneratedRendition rendition, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new MemoryStream(rendition.Bytes);

            await imagesClient.UploadImageV1Async(
                rendition.Hash,
                new FileParameter(content, rendition.Hash, "image/webp"),
                cancellationToken);

            return rendition.Hash;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested is false)
        {
            return null;
        }
    }
}
