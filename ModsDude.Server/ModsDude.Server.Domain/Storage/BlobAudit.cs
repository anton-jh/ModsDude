namespace ModsDude.Server.Domain.Storage;

/// <param name="Unreferenced">Blobs whose name parses and that nothing refers to.</param>
/// <param name="Unrecognised">Blobs whose name does not parse. Never deleted.</param>
/// <param name="Missing">Names the database refers to that the listing did not hold. Each is checked again before it counts.</param>
/// <param name="Unlabelled">Referenced blobs that carry no <see cref="BlobLabel"/>. Always empty for images, which are not labelled.</param>
public sealed record BlobAuditResult(
    IReadOnlyList<StoredBlob> Unreferenced,
    IReadOnlyList<StoredBlob> Unrecognised,
    IReadOnlyList<string> Missing,
    IReadOnlyList<StoredBlob> Unlabelled);

/// <summary>
/// Compares one container's listing with the names the database refers to, in both directions. Pure,
/// and it decides nothing about deleting: the audit only reports.
/// </summary>
/// <remarks>
/// The caller lists the container before it reads the references, so a blob registered while the
/// listing ran reads as referenced. That same order makes such a blob look missing, which is why a
/// missing name is checked again on its own before it counts.
/// </remarks>
public static class BlobAudit
{
    public static BlobAuditResult Classify(StorageContainer container, IEnumerable<StoredBlob> stored, IReadOnlySet<string> referenced)
    {
        var unreferenced = new List<StoredBlob>();
        var unrecognised = new List<StoredBlob>();
        var unlabelled = new List<StoredBlob>();
        var listed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var blob in stored)
        {
            listed.Add(blob.Name);

            if (referenced.Contains(blob.Name))
            {
                if (container is not StorageContainer.Images && !BlobLabel.IsPresentIn(blob.Metadata))
                {
                    unlabelled.Add(blob);
                }
            }
            else if (BlobNames.IsRecognised(container, blob.Name))
            {
                unreferenced.Add(blob);
            }
            else
            {
                unrecognised.Add(blob);
            }
        }

        return new BlobAuditResult(
            [.. unreferenced.OrderBy(x => x.Name, StringComparer.Ordinal)],
            [.. unrecognised.OrderBy(x => x.Name, StringComparer.Ordinal)],
            [.. referenced.Where(x => !listed.Contains(x)).Order(StringComparer.Ordinal)],
            [.. unlabelled.OrderBy(x => x.Name, StringComparer.Ordinal)]);
    }
}
