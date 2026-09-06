using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using System.Text;

namespace ModsDude.Client.Core.Tests.Import;

/// <summary>
/// The thing that decides whether a duplicate is a duplicate. It ends with somebody's file in the
/// Recycle Bin, so the property that matters is that it never calls two different files the same.
/// </summary>
public class ModOccurrenceResolverTests
{
    private static readonly ModSource _downloads =
        new(ModSourceId.Downloads, "Downloads", @"C:\Downloads", ModSourceKind.Downloads);

    private static readonly ModSource _instance =
        new(ModSourceId.ForInstance(Guid.NewGuid()), "FS25", @"C:\FS25\mods", ModSourceKind.Instance);

    private static readonly ModSource _added =
        new(ModSourceId.ForFolder(@"D:\Backup"), "Backup", @"D:\Backup", ModSourceKind.AdHoc);


    [Fact]
    public async Task A_version_from_one_source_is_one_candidate_and_is_never_hashed()
    {
        var opened = 0;

        var candidates = await ModOccurrenceResolver.ResolveAsync(
            [Occurrence(_downloads, "bytes", () => opened++)],
            CancellationToken.None);

        var candidate = Assert.Single(candidates);

        Assert.Null(candidate.ContentHash);
        Assert.Equal(0, opened);
    }

    /// <summary>
    /// The ordinary case: the same file in the mod folder and still in Downloads. One candidate, so
    /// nothing is asked and nothing is recycled.
    /// </summary>
    [Fact]
    public async Task Identical_bytes_in_several_sources_collapse_into_one_candidate()
    {
        var candidates = await ModOccurrenceResolver.ResolveAsync(
            [
                Occurrence(_downloads, "one build"),
                Occurrence(_instance, "one build"),
                Occurrence(_added, "one build")
            ],
            CancellationToken.None);

        var candidate = Assert.Single(candidates);

        Assert.Equal(3, candidate.Occurrences.Count);
        Assert.NotNull(candidate.ContentHash);
    }

    [Fact]
    public async Task Differing_bytes_are_separate_candidates()
    {
        var candidates = await ModOccurrenceResolver.ResolveAsync(
            [
                Occurrence(_downloads, "one build"),
                Occurrence(_instance, "another build")
            ],
            CancellationToken.None);

        Assert.Equal(2, candidates.Count);
        Assert.Distinct(candidates.Select(x => x.Key));
    }

    /// <summary>
    /// The case the old size comparison got wrong, and the reason this hashes: two builds that
    /// happen to weigh the same are still two builds.
    /// </summary>
    [Fact]
    public async Task Equal_sizes_are_not_taken_for_equal_bytes()
    {
        var candidates = await ModOccurrenceResolver.ResolveAsync(
            [
                Occurrence(_downloads, "aaaa"),
                Occurrence(_instance, "bbbb")
            ],
            CancellationToken.None);

        Assert.Equal(2, candidates.Count);
    }

    /// <summary>
    /// A copy that has gone since the scan is dropped rather than failing the version - the other
    /// copy is very often still there, and that is the one the import wanted.
    /// </summary>
    [Fact]
    public async Task An_unreadable_copy_is_dropped_rather_than_taking_the_version_with_it()
    {
        var candidates = await ModOccurrenceResolver.ResolveAsync(
            [
                new ModOccurrence(_downloads, @"C:\Downloads\gone.zip", 9, () => throw new FileNotFoundException()),
                Occurrence(_instance, "one build")
            ],
            CancellationToken.None);

        var candidate = Assert.Single(candidates);

        Assert.Equal(_instance, candidate.Primary.Source);
    }

    [Fact]
    public async Task Nothing_readable_is_no_candidates()
    {
        var candidates = await ModOccurrenceResolver.ResolveAsync(
            [
                new ModOccurrence(_downloads, @"C:\Downloads\gone.zip", 9, () => throw new FileNotFoundException()),
                new ModOccurrence(_instance, @"C:\FS25\mods\gone.zip", 9, () => throw new IOException())
            ],
            CancellationToken.None);

        Assert.Empty(candidates);
    }


    private static ModOccurrence Occurrence(ModSource source, string content, Action? onOpen = null)
    {
        var bytes = Encoding.UTF8.GetBytes(content);

        return new ModOccurrence(source, Path.Combine(source.Path, "FS25_Plough.zip"), bytes.Length, () =>
        {
            onOpen?.Invoke();
            return new MemoryStream(bytes);
        });
    }
}
