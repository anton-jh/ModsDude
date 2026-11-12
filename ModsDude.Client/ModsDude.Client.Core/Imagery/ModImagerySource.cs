using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Collections.Concurrent;

namespace ModsDude.Client.Core.Imagery;

/// <summary>What a version renders: an icon for a list row, and a gallery for the details dialog.</summary>
public record ModVersionImagery(ModImage? Icon, IReadOnlyList<ModImage> Images)
{
    public static readonly ModVersionImagery None = new(null, []);
}


/// <summary>
/// Decides where a version's imagery comes from. The rule is keyed on whether the version is
/// registered, not on whether its file happens to be on this machine.
/// </summary>
public interface IModImagerySource
{
    /// <summary>Whatever can be resolved without doing any work. Safe on the UI thread.</summary>
    ModVersionImagery Get(CatalogModVersion version);

    /// <summary>
    /// The same, except that a registered version with no derivatives and a file here has them
    /// generated and uploaded first.
    /// </summary>
    Task<ModVersionImagery> GetAsync(Guid repoId, CatalogModVersion version, CancellationToken cancellationToken);
}


/// <inheritdoc cref="IModImagerySource"/>
public class ModImagerySource(
    IModImageStore store,
    IModImageBackfill backfill,
    ILogger<ModImagerySource> logger)
    : IModImagerySource
{
    /// <summary>
    /// A row can be realized, recycled and realized again while scrolling, and every version of one
    /// mod is usually missing its imagery at the same moment. One attempt per version per session is
    /// enough - the whole point of backfill is that somebody else's client closes the gap too.
    /// </summary>
    private readonly ConcurrentDictionary<(Guid, ModKey, ModVersionKey), Task<IReadOnlyList<ModImageReference>>> _backfills = new();


    public ModVersionImagery Get(CatalogModVersion version)
    {
        // Registered means the repo's derivatives, even with the file sitting right here. Anything
        // else costs a per-row archive open and a managed BC7 decode for resolution nobody wants,
        // gives cache keys that change when the file moves, and makes one list render half from
        // originals and half from derivatives.
        return version.IsOnServer
            ? FromReferences(version.ServerImages)
            : new ModVersionImagery(version.Icon, version.Images);
    }

    public async Task<ModVersionImagery> GetAsync(Guid repoId, CatalogModVersion version, CancellationToken cancellationToken)
    {
        var resolved = Get(version);

        if (version.IsOnServer is false || resolved.Icon is not null || resolved.Images.Count > 0)
        {
            return resolved;
        }

        if (TryGetLocalCopy(version) is not LocalMod local)
        {
            // Registered, no derivatives, and no file here to make them from. Initials, exactly as
            // for a local mod that ships without an icon.
            return ModVersionImagery.None;
        }

        var references = await _backfills.GetOrAdd(
            (repoId, version.ModId, version.VersionId),
            key => BackfillAsync(key.Item1, version, local));

        return FromReferences(references);
    }


    private async Task<IReadOnlyList<ModImageReference>> BackfillAsync(Guid repoId, CatalogModVersion version, LocalMod local)
    {
        // Another build wearing the same id and version would publish its images as this one's, for
        // everybody, so only a copy holding the registered bytes is used.
        if (await IsRegisteredBuildAsync(version, local) is false)
        {
            return [];
        }

        return await backfill.BackfillAsync(repoId, version.ModId, version.VersionId, local, CancellationToken.None);
    }

    private async Task<bool> IsRegisteredBuildAsync(CatalogModVersion version, LocalMod local)
    {
        if (version.ContentHash is null || (version.SizeBytes is long size && size != local.FileLength))
        {
            return false;
        }

        try
        {
            await using var content = local.GetStream();

            return ModContentHasher.Matches(await ModContentHasher.ComputeAsync(content, CancellationToken.None), version.ContentHash);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not read {File} to backfill the imagery of {Mod} {Version}.", local.FilePath, version.ModId.Value, version.VersionId.Value);

            return false;
        }
    }


    private ModVersionImagery FromReferences(IReadOnlyList<ModImageReference> references)
    {
        if (references.Count == 0)
        {
            return ModVersionImagery.None;
        }

        // An icon is one image with the same two renditions as any other, so it decodes the same way
        // - which is what gives a details dialog for a mod that ships no store images something
        // larger than 128 px to draw.
        var iconRenditions = references.Where(x => x.Kind is ModImageKind.Icon).ToList();

        var images = references
            .Where(x => x.Kind is ModImageKind.StoreImage)
            .GroupBy(x => x.Position)
            .OrderBy(x => x.Key)
            .Select(CreateGalleryImage)
            .ToList();

        return new ModVersionImagery(
            iconRenditions.Count > 0 ? CreateGalleryImage(iconRenditions) : null,
            images);
    }

    /// <summary>
    /// One gallery entry out of the renditions that made it up. A half-published pair still renders:
    /// whichever rendition is there stands in for the other, so an interrupted upload costs sharpness
    /// rather than the image.
    /// </summary>
    private ModImage CreateGalleryImage(IEnumerable<ModImageReference> renditions)
    {
        var byRendition = renditions.ToLookup(x => x.Rendition);

        var thumbnail = byRendition[ModImageRendition.Thumbnail].FirstOrDefault();
        var full = byRendition[ModImageRendition.Full].FirstOrDefault();

        var small = CreateImage(thumbnail ?? full!);

        return full is null || thumbnail is null
            ? small
            : small with { FullSize = CreateImage(full) };
    }

    private ModImage CreateImage(ModImageReference reference)
    {
        // The address is the cache key, which is the one key that can never invalidate: the bytes
        // it names are the bytes it is a hash of.
        return new ModImage(reference.FileName, reference.Hash, ct => store.GetAsync(reference.Hash, ct))
        {
            IsPreSized = true
        };
    }

    /// <summary>
    /// The copy a registered version's missing imagery could be derived from, where a source still
    /// holds the file and it ships any. Not one of several copies that disagree.
    /// </summary>
    private static LocalMod? TryGetLocalCopy(CatalogModVersion version)
    {
        if (version.HasSourceConflict || version.FoundIn.FirstOrDefault() is not ModOccurrence occurrence)
        {
            return null;
        }

        return occurrence.Mod.Icon is null && occurrence.Mod.Images.Count == 0 ? null : occurrence.Mod;
    }
}
