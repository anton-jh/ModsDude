using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Sync;
using System.Text.Json;

namespace ModsDude.Client.Core.Tests.Sync;

public class SyncManifestStoreTests
{
    [Fact]
    public void A_manifest_survives_a_round_trip_intact()
    {
        using var directory = new TempDirectory("manifests");
        var store = new SyncManifestStore(directory.Path);

        var manifest = Manifest(Keys.Game(), "C:\\games\\mods", [
            new SyncManifestEntry("fs25_a", "1.0.0", new string('a', 64), "fs25_a.zip", 4096, DateTimeOffset.UtcNow),
            new SyncManifestEntry("fs25_b", "2.1.0", new string('b', 64), "fs25_b.zip", 8192, DateTimeOffset.UtcNow.AddDays(-3))
        ]);

        store.Write(manifest);

        var read = store.TryRead(manifest.Game);

        Assert.NotNull(read);
        Assert.Equal(manifest.ProfileId, read.ProfileId);
        Assert.Equal(manifest.RepoId, read.RepoId);
        Assert.Equal(manifest.ModFolder, read.ModFolder);
        Assert.Equal(manifest.Entries, read.Entries);

        // The times have to survive exactly, because the cheap drift check compares them against a
        // directory listing - a manifest that rounded them would report drift on every launch.
        Assert.Equal(manifest.Entries[0].ModifiedUtc, read.Entries[0].ModifiedUtc);
    }

    [Fact]
    public void One_file_per_game_beside_state_json()
    {
        using var directory = new TempDirectory("manifests-per-game");
        var store = new SyncManifestStore(directory.Path);

        var first = Manifest(Keys.Game("fs25"), "C:\\one", []);
        var second = Manifest(Keys.Game("fs22"), "D:\\two", []);

        store.Write(first);
        store.Write(second);

        Assert.Equal("C:\\one", store.TryRead(first.Game)?.ModFolder);
        Assert.Equal("D:\\two", store.TryRead(second.Game)?.ModFolder);
        Assert.True(File.Exists(Path.Combine(directory.Path, $"{first.Game}.json")));
    }

    [Fact]
    public void An_absent_or_unreadable_manifest_reads_as_none()
    {
        using var directory = new TempDirectory("manifests-unreadable");
        var store = new SyncManifestStore(directory.Path);
        var game = Keys.Game();

        Assert.Null(store.TryRead(game));

        File.WriteAllText(Path.Combine(directory.Path, $"{game}.json"), "{ not json");

        // Losing a manifest costs a rescan and nothing else, so there is nothing here to repair or
        // report - it is simply absent.
        Assert.Null(store.TryRead(game));
    }

    [Fact]
    public void A_manifest_from_an_incompatible_version_is_discarded()
    {
        using var directory = new TempDirectory("manifests-version");
        var store = new SyncManifestStore(directory.Path);
        var game = Keys.Game();

        var manifest = Manifest(game, "C:\\games\\mods", []) with { Version = SyncManifest.CurrentVersion + 1 };

        File.WriteAllText(
            Path.Combine(directory.Path, $"{game}.json"),
            JsonSerializer.Serialize(manifest));

        Assert.Null(store.TryRead(game));
    }

    [Fact]
    public void Rewriting_replaces_the_previous_manifest_in_place()
    {
        using var directory = new TempDirectory("manifests-rewrite");
        var store = new SyncManifestStore(directory.Path);
        var game = Keys.Game();

        store.Write(Manifest(game, "C:\\before", []));
        store.Write(Manifest(game, "C:\\after", []));

        Assert.Equal("C:\\after", store.TryRead(game)?.ModFolder);

        // Written through a temp file and moved into place, so nothing is left half-written beside it.
        Assert.Single(Directory.EnumerateFiles(directory.Path));
    }


    private static SyncManifest Manifest(GameIdentity game, string modFolder, IReadOnlyList<SyncManifestEntry> entries)
    {
        return new SyncManifest
        {
            Game = game,
            RepoId = Guid.NewGuid(),
            ProfileId = Guid.NewGuid(),
            SyncedAt = DateTimeOffset.UtcNow,
            ModFolder = modFolder,
            Entries = entries
        };
    }
}
