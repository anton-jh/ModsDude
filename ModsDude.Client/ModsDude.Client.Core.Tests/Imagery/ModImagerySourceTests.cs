using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Tests.Imagery;

public class ModImagerySourceTests
{
    private static readonly Guid _repoId = Guid.NewGuid();

    private const string _serverIconHash = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string _serverStoreFullHash = "2222222222222222222222222222222222222222222222222222222222222222";
    private const string _serverStoreThumbnailHash = "3333333333333333333333333333333333333333333333333333333333333333";


    [Fact]
    public void An_unregistered_version_renders_from_its_archive()
    {
        var version = CreateVersion(isOnServer: false, withLocalFile: true);

        var imagery = CreateSource().Get(version);

        Assert.Equal(version.Icon, imagery.Icon);
        Assert.Equal(version.Images, imagery.Images);
    }

    [Fact]
    public void A_registered_version_renders_from_the_repo_even_with_the_file_right_here()
    {
        // Keyed on registration, not on local availability: hunting for the file costs a per-row
        // archive open and a managed BC7 decode for resolution nobody wants in a 96 px strip.
        var version = CreateVersion(isOnServer: true, withLocalFile: true) with { ServerImages = CreateReferences() };

        var imagery = CreateSource().Get(version);

        Assert.Equal(_serverIconHash, imagery.Icon?.CacheKey);
        Assert.Equal(_serverStoreThumbnailHash, Assert.Single(imagery.Images).CacheKey);
    }

    [Fact]
    public void A_server_image_is_keyed_by_its_own_address_and_needs_no_size()
    {
        var version = CreateVersion(isOnServer: true, withLocalFile: false) with { ServerImages = CreateReferences() };

        var imagery = CreateSource().Get(version);

        Assert.True(imagery.Icon!.IsPreSized);
        Assert.Equal(_serverIconHash, imagery.Icon.CacheKey);
    }

    [Fact]
    public void A_gallery_entry_carries_both_renditions()
    {
        var version = CreateVersion(isOnServer: true, withLocalFile: false) with { ServerImages = CreateReferences() };

        var image = Assert.Single(CreateSource().Get(version).Images);

        Assert.Equal(_serverStoreThumbnailHash, image.CacheKey);
        Assert.Equal(_serverStoreFullHash, image.FullSize?.CacheKey);
    }

    [Fact]
    public async Task A_registered_version_with_no_imagery_and_no_file_here_renders_as_initials()
    {
        var version = CreateVersion(isOnServer: true, withLocalFile: false);
        var backfill = new FakeBackfill(CreateReferences());

        var imagery = await CreateSource(backfill).GetAsync(_repoId, version, CancellationToken.None);

        Assert.Null(imagery.Icon);
        Assert.Empty(imagery.Images);
        Assert.Equal(0, backfill.Calls);
    }

    [Fact]
    public async Task A_registered_version_with_no_imagery_and_the_file_here_closes_the_gap()
    {
        // The fix for a version registered without imagery is not a local fallback: the client that
        // holds the file is the one that should generate and upload what is missing, for everyone.
        var version = CreateVersion(isOnServer: true, withLocalFile: true);
        var backfill = new FakeBackfill(CreateReferences());

        var imagery = await CreateSource(backfill).GetAsync(_repoId, version, CancellationToken.None);

        Assert.Equal(1, backfill.Calls);
        Assert.Equal(_serverIconHash, imagery.Icon?.CacheKey);
    }

    [Fact]
    public async Task A_version_is_only_backfilled_once()
    {
        var version = CreateVersion(isOnServer: true, withLocalFile: true);
        var backfill = new FakeBackfill(CreateReferences());
        var source = CreateSource(backfill);

        await source.GetAsync(_repoId, version, CancellationToken.None);
        await source.GetAsync(_repoId, version, CancellationToken.None);

        Assert.Equal(1, backfill.Calls);
    }

    [Fact]
    public async Task A_registered_version_that_already_has_imagery_is_never_backfilled()
    {
        var version = CreateVersion(isOnServer: true, withLocalFile: true) with { ServerImages = CreateReferences() };
        var backfill = new FakeBackfill([]);

        await CreateSource(backfill).GetAsync(_repoId, version, CancellationToken.None);

        Assert.Equal(0, backfill.Calls);
    }


    private static ModImagerySource CreateSource(IModImageBackfill? backfill = null)
    {
        return new ModImagerySource(new FakeStore(), backfill ?? new FakeBackfill([]));
    }

    private static IReadOnlyList<ModImageReference> CreateReferences()
    {
        return
        [
            new ModImageReference(_serverIconHash, ModImageKind.Icon, 0, "icon.dds"),
            new ModImageReference(_serverStoreFullHash, ModImageKind.StoreImage,
                ModImageReferenceLayout.GetPosition(0, ModImageDerivative.Full), "store_01.dds"),
            new ModImageReference(_serverStoreThumbnailHash, ModImageKind.StoreImage,
                ModImageReferenceLayout.GetPosition(0, ModImageDerivative.Thumbnail), "store_01.dds")
        ];
    }

    private static CatalogModVersion CreateVersion(bool isOnServer, bool withLocalFile)
    {
        var version = new CatalogModVersion(
            ModKey.From("fs25_amod"),
            ModVersionKey.From("1.0.0.0"),
            "A Mod",
            "It does things.",
            IsLocal: withLocalFile,
            IsOnServer: isOnServer,
            Locked: false);

        if (withLocalFile is false)
        {
            return version;
        }

        var source = new ModSource(ModSourceId.Downloads, "Downloads", @"C:\Downloads", ModSourceKind.Downloads);

        return version with
        {
            Icon = new ModImage("icon.dds", "local-icon", _ => Task.FromResult<byte[]>([1])),
            Images = [new ModImage("store_01.dds", "local-store", _ => Task.FromResult<byte[]>([2]))],
            FoundIn = [new ModOccurrence(source, @"C:\Downloads\FS25_AMod.zip", 1024, () => new MemoryStream())]
        };
    }


    private class FakeStore : IModImageStore
    {
        public Task<byte[]> GetAsync(string hash, CancellationToken cancellationToken) => Task.FromResult<byte[]>([]);

        public Task PutAsync(string hash, byte[] bytes, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private class FakeBackfill(IReadOnlyList<ModImageReference> references) : IModImageBackfill
    {
        public int Calls { get; private set; }


        public Task<IReadOnlyList<ModImageReference>> BackfillAsync(
            Guid repoId, ModKey modId, ModVersionKey versionId, LocalMod mod, CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(references);
        }
    }
}
