using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Mods;

public class ModVersion
{
    private readonly List<ModImageReference> _images = [];


    public required RepoId RepoId { get; init; }
    public required ModId ModId { get; init; }
    public required ModVersionId Id { get; init; }

    public required int SequenceNumber { get; set; }
    public required string DisplayName { get; set; }
    public required string Description { get; set; }

    /// <summary>
    /// SHA-256 of the mod file. A real property rather than a <see cref="ModAttribute"/>: the
    /// system depends on it to address content, so it cannot be something an adapter may omit.
    /// </summary>
    public required string ContentHash { get; set; }

    /// <summary>
    /// The mod itself is version-sensitive, as determined by the adapter from the mod file at
    /// registration. The per-profile override lives on ModDependency.
    /// </summary>
    public required bool Locked { get; set; }

    public required HashSet<ModAttribute> Attributes { get; init; }
    public IReadOnlyList<ModImageReference> Images => _images;

    public required DateTimeOffset Created { get; init; }
    public required DateTimeOffset Updated { get; set; }


    /// <summary>
    /// Whether a set of references describes a coherent gallery: one icon at most, and no two images
    /// of a kind claiming the same position. A version's imagery is arrived at by several
    /// independent uploaders — the importer, and any client that opportunistically backfills — so
    /// the ordering is only meaningful if it cannot be self-contradictory.
    /// </summary>
    public static bool CheckImagesAreValid(IReadOnlyCollection<ModImageReference> images)
    {
        return images.Count(x => x.Kind == ModImageKind.Icon) <= 1
            && !images.GroupBy(x => (x.Kind, x.Position)).Any(x => x.Count() > 1);
    }

    /// <summary>
    /// Replaces the whole set rather than adding to it. Imagery is uploaded best-effort after
    /// registration, so it arrives late, in unknown completeness, and possibly more than once when a
    /// client retries or a backfill fires; a replace is the only shape of that which is idempotent.
    /// Call <see cref="CheckImagesAreValid"/> first.
    /// </summary>
    public void SetImages(IReadOnlyCollection<ModImageReference> images, DateTimeOffset timestamp)
    {
        if (!CheckImagesAreValid(images))
        {
            throw new InvalidOperationException($"Cannot set images on version '{Id.Value}' of mod '{ModId.Value}'. The set has more than one icon, or two images of a kind at the same position");
        }

        _images.Clear();
        _images.AddRange(images);

        Updated = timestamp;
    }
}


public readonly record struct ModVersionId(string Value);
