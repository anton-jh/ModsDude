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
    IModImageBackfill backfill)
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

        if (TryBuildLocalMod(version) is not LocalMod local)
        {
            // Registered, no derivatives, and no file here to make them from. Initials, exactly as
            // for a local mod that ships without an icon.
            return ModVersionImagery.None;
        }

        var references = await _backfills.GetOrAdd(
            (repoId, version.ModId, version.VersionId),
            key => backfill.BackfillAsync(key.Item1, key.Item2, key.Item3, local, CancellationToken.None));

        return FromReferences(references);
    }


    private ModVersionImagery FromReferences(IReadOnlyList<ModImageReference> references)
    {
        if (references.Count == 0)
        {
            return ModVersionImagery.None;
        }

        var icon = references.FirstOrDefault(x => x.Kind is ModImageKind.Icon) is ModImageReference reference
            ? CreateImage(reference)
            : null;

        var images = references
            .Where(x => x.Kind is ModImageKind.StoreImage)
            .GroupBy(x => ModImageReferenceLayout.GetIndex(x.Position))
            .OrderBy(x => x.Key)
            .Select(CreateGalleryImage)
            .ToList();

        return new ModVersionImagery(icon, images);
    }

    /// <summary>
    /// One gallery entry out of the renditions that made it up. A half-published pair still renders:
    /// whichever rendition is there stands in for the other, so an interrupted upload costs sharpness
    /// rather than the image.
    /// </summary>
    private ModImage CreateGalleryImage(IEnumerable<ModImageReference> renditions)
    {
        var byDerivative = renditions.ToLookup(x => ModImageReferenceLayout.GetDerivative(x.Position));

        var thumbnail = byDerivative[ModImageDerivative.Thumbnail].FirstOrDefault();
        var full = byDerivative[ModImageDerivative.Full].FirstOrDefault();

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
    /// The archive record a registered version was merged from, where a source still holds the file.
    /// Only ever used to derive what the repo is missing.
    /// </summary>
    private static LocalMod? TryBuildLocalMod(CatalogModVersion version)
    {
        if (version.Icon is null && version.Images.Count == 0)
        {
            return null;
        }

        if (version.FoundIn.FirstOrDefault() is not ModOccurrence occurrence || version.OpenStream is null)
        {
            return null;
        }

        return new LocalMod(version.ModId, version.VersionId, version.Name, version.Description, version.OpenStream)
        {
            FilePath = occurrence.FilePath,
            FileLength = occurrence.FileLength,
            Icon = version.Icon,
            Images = version.Images,
            Author = version.Author
        };
    }
}
