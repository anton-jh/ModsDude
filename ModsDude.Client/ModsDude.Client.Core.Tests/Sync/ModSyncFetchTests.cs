using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Sync;

/// <summary>
/// Downloading a profile's mods ahead of applying it, and what a mod the server has no file for
/// does to either.
/// </summary>
public class ModSyncFetchTests
{
    [Fact]
    public async Task Fetching_fills_the_store_and_leaves_the_folder_alone()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Install("fs25_mine.zip", Mod("1.0.0", "mine"));

        var result = await fixture.FetchAsync();

        Assert.True(result.Completed);
        Assert.True(fixture.ServingStore.Contains(SyncTestContent.HashOf(Mod("1.0.0", "a"))));
        Assert.Equal(["fs25_mine.zip"], fixture.FolderContents());
        Assert.Null(fixture.Manifests.TryRead(fixture.Target));
    }

    [Fact]
    public async Task An_apply_after_fetching_downloads_nothing()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        await fixture.FetchAsync();
        var downloadsBefore = fixture.Downloader.Downloads;

        var plan = await fixture.PlanAsync();

        Assert.Empty(plan.HashesToFetch);
        Assert.True((await fixture.ExecuteAsync(plan)).Completed);
        Assert.Equal(downloadsBefore, fixture.Downloader.Downloads);
    }

    [Fact]
    public async Task Fetching_again_downloads_nothing()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        await fixture.FetchAsync();
        var downloadsBefore = fixture.Downloader.Downloads;

        Assert.True((await fixture.FetchAsync()).Completed);
        Assert.Equal(downloadsBefore, fixture.Downloader.Downloads);
    }

    [Fact]
    public async Task A_mod_the_server_has_no_file_for_fails_the_fetch_as_missing()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Server.Pin("fs25_b", "1.0.0", Mod("1.0.0", "b"));
        fixture.Server.LoseFile("fs25_b");

        var result = await fixture.FetchAsync();

        Assert.False(result.Completed);
        var failure = Assert.Single(result.Failures);
        Assert.True(failure.MissingOnServer);
        Assert.Empty(fixture.FolderContents());
    }

    [Fact]
    public async Task An_apply_with_a_mod_missing_on_the_server_says_so_and_touches_nothing()
    {
        using var fixture = new SyncFixture();
        fixture.Install("fs25_mine.zip", Mod("1.0.0", "mine"));
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        fixture.Server.LoseFile("fs25_a");

        var result = await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.False(result.Completed);
        Assert.All(result.Failures, x => Assert.True(x.MissingOnServer));
        Assert.Equal(["fs25_mine.zip"], fixture.FolderContents());
        Assert.Empty(fixture.RecycleBin.Recycled);
    }

    [Fact]
    public async Task A_cancelled_fetch_throws_and_leaves_the_folder_alone()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        using var cancel = new CancellationTokenSource();
        fixture.Downloader.BeforeDownload = cancel.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.FetchAsync(
            new ModFetchRequest(fixture.Server.RepoId, fixture.Server.ProfileId, null, [fixture.Folder.Path]), null, cancel.Token));

        Assert.Empty(fixture.FolderContents());
    }


    private static string Mod(string version, string build) => SyncTestContent.File(version, build);
}
