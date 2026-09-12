using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
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

        var manifest = Manifest(Keys.Target(), "C:\\games\\mods", [
            new SyncManifestEntry("fs25_a", "1.0.0", new string('a', 64), "fs25_a.zip", 4096, DateTimeOffset.UtcNow),
            new SyncManifestEntry("fs25_b", "2.1.0", new string('b', 64), "fs25_b.zip", 8192, DateTimeOffset.UtcNow.AddDays(-3))
        ]);

        store.Write(manifest);

        var read = store.TryRead(manifest.Target);

        Assert.NotNull(read);
        Assert.Equal(manifest.Target, read.Target);
        Assert.Equal(manifest.ProfileId, read.ProfileId);
        Assert.Equal(manifest.RepoId, read.RepoId);
        Assert.Equal(manifest.ModFolder, read.ModFolder);
        Assert.Equal(manifest.Entries, read.Entries);

        // The times have to survive exactly, because the cheap drift check compares them against a
        // directory listing - a manifest that rounded them would report drift on every launch.
        Assert.Equal(manifest.Entries[0].ModifiedUtc, read.Entries[0].ModifiedUtc);
    }

    /// <summary>
    /// The target key has to survive the round trip as a key rather than as a blank, which is what a
    /// record struct with a validating constructor does through the default serializer.
    /// </summary>
    [Fact]
    public void The_target_key_comes_back_as_a_key_somebody_can_read()
    {
        using var directory = new TempDirectory("manifests-key");
        var store = new SyncManifestStore(directory.Path);

        store.Write(Manifest(Keys.Target("server"), "C:\\server\\mods", []));

        Assert.Equal("server", store.TryRead(Keys.Target("server"))?.Target.Key.Value);
    }

    [Fact]
    public void One_file_per_game_beside_state_json()
    {
        using var directory = new TempDirectory("manifests-per-game");
        var store = new SyncManifestStore(directory.Path);

        var first = Manifest(Keys.Target(discriminator: "fs25"), "C:\\one", []);
        var second = Manifest(Keys.Target(discriminator: "fs22"), "D:\\two", []);

        store.Write(first);
        store.Write(second);

        Assert.Equal("C:\\one", store.TryRead(first.Target)?.ModFolder);
        Assert.Equal("D:\\two", store.TryRead(second.Target)?.ModFolder);
        Assert.True(File.Exists(Path.Combine(directory.Path, $"{Name(first.Target)}.json")));
    }

    /// <summary>
    /// The whole point of the re-keying: syncing the dedicated server must not rewrite the record of
    /// what the MP client is running.
    /// </summary>
    [Fact]
    public void Two_targets_of_one_game_keep_two_manifests()
    {
        using var directory = new TempDirectory("manifests-per-target");
        var store = new SyncManifestStore(directory.Path);

        store.Write(Manifest(Keys.Target("server"), "C:\\server\\mods", []));
        store.Write(Manifest(Keys.Target("client"), "C:\\client\\mods", []));

        Assert.Equal("C:\\server\\mods", store.TryRead(Keys.Target("server"))?.ModFolder);
        Assert.Equal("C:\\client\\mods", store.TryRead(Keys.Target("client"))?.ModFolder);
        Assert.Equal(2, Directory.EnumerateFiles(directory.Path).Count());
    }

    /// <summary>
    /// A target that was emptied out of the settings and one whose key an adapter author renamed are
    /// the same file on disk, and neither is ever looked for again.
    /// </summary>
    [Fact]
    public void A_manifest_for_another_target_is_not_this_targets()
    {
        using var directory = new TempDirectory("manifests-other-target");
        var store = new SyncManifestStore(directory.Path);

        store.Write(Manifest(Keys.Target("mp"), "C:\\mp\\mods", []));

        Assert.Null(store.TryRead(Keys.Target("multiplayer")));
    }

    [Fact]
    public void An_absent_or_unreadable_manifest_reads_as_none()
    {
        using var directory = new TempDirectory("manifests-unreadable");
        var store = new SyncManifestStore(directory.Path);
        var target = Keys.Target();

        Assert.Null(store.TryRead(target));

        File.WriteAllText(Path.Combine(directory.Path, $"{Name(target)}.json"), "{ not json");

        // Losing a manifest costs a rescan and nothing else, so there is nothing here to repair or
        // report - it is simply absent.
        Assert.Null(store.TryRead(target));
    }

    [Fact]
    public void A_manifest_from_an_incompatible_version_is_discarded()
    {
        using var directory = new TempDirectory("manifests-version");
        var store = new SyncManifestStore(directory.Path);
        var target = Keys.Target();

        var manifest = Manifest(target, "C:\\games\\mods", []) with { Version = SyncManifest.CurrentVersion + 1 };

        File.WriteAllText(
            Path.Combine(directory.Path, $"{Name(target)}.json"),
            JsonSerializer.Serialize(manifest));

        Assert.Null(store.TryRead(target));
    }

    [Fact]
    public void Rewriting_replaces_the_previous_manifest_in_place()
    {
        using var directory = new TempDirectory("manifests-rewrite");
        var store = new SyncManifestStore(directory.Path);
        var target = Keys.Target();

        store.Write(Manifest(target, "C:\\before", []));
        store.Write(Manifest(target, "C:\\after", []));

        Assert.Equal("C:\\after", store.TryRead(target)?.ModFolder);

        // Written through a temp file and moved into place, so nothing is left half-written beside it.
        Assert.Single(Directory.EnumerateFiles(directory.Path));
    }

    /// <summary>
    /// The orphan half that is droppable. Emptying a target's settings field takes the target away,
    /// and the manifest behind it describes a folder nothing will ever ask about again.
    /// </summary>
    [Fact]
    public void A_manifest_for_a_target_that_is_gone_is_dropped()
    {
        using var directory = new TempDirectory("manifests-stale");
        var store = new SyncManifestStore(directory.Path);

        store.Write(Manifest(Keys.Target("server"), "C:\\server\\mods", []));
        store.Write(Manifest(Keys.Target("client"), "C:\\client\\mods", []));

        // The settings now fill in one field where they filled in two.
        store.DropStale([Keys.Target("server")]);

        Assert.NotNull(store.TryRead(Keys.Target("server")));
        Assert.Null(store.TryRead(Keys.Target("client")));
    }

    /// <summary>
    /// Everything unexpected, not only the keys that have gone: a file an older version wrote under
    /// a name nothing builds any more is the same kind of orphan, and this is what collects it.
    /// </summary>
    [Fact]
    public void A_file_the_store_would_never_have_written_is_dropped_too()
    {
        using var directory = new TempDirectory("manifests-orphans");
        var store = new SyncManifestStore(directory.Path);

        store.Write(Manifest(Keys.Target(), "C:\\mods", []));

        // What slice 2a left behind - named by the game alone - and what an interrupted atomic write
        // leaves beside a live manifest.
        File.WriteAllText(Path.Combine(directory.Path, $"{Keys.Game()}.json"), "{}");
        File.WriteAllText(Path.Combine(directory.Path, $"{Name(Keys.Target())}.json.tmp"), "{}");

        store.DropStale([Keys.Target()]);

        Assert.Equal([$"{Name(Keys.Target())}.json"], Directory.EnumerateFiles(directory.Path).Select(Path.GetFileName));
        Assert.NotNull(store.TryRead(Keys.Target()));
    }

    /// <summary>
    /// A game disconnected, or a state file discarded by a version bump. Expecting nothing is an
    /// ordinary answer and costs a rescan, which is all a manifest is ever worth.
    /// </summary>
    [Fact]
    public void Expecting_nothing_drops_everything()
    {
        using var directory = new TempDirectory("manifests-none-expected");
        var store = new SyncManifestStore(directory.Path);

        store.Write(Manifest(Keys.Target(), "C:\\mods", []));
        store.DropStale([]);

        Assert.Empty(Directory.EnumerateFiles(directory.Path));
    }

    /// <summary>A sweep of a directory nothing has written to yet is not an error.</summary>
    [Fact]
    public void Sweeping_before_anything_has_been_written_does_nothing()
    {
        using var directory = new TempDirectory("manifests-unwritten");

        new SyncManifestStore(Path.Combine(directory.Path, "not-there")).DropStale([Keys.Target()]);
    }

    /// <summary>
    /// What the savegame side reads, since a binding is keyed on the game and not yet on a target.
    /// </summary>
    [Fact]
    public void Targets_that_agree_answer_with_the_revision_they_agree_on()
    {
        using var directory = new TempDirectory("manifests-agreed");
        var store = new SyncManifestStore(directory.Path);
        var profile = Guid.NewGuid();

        store.Write(Manifest(Keys.Target("server"), "C:\\server", []) with { ProfileId = profile, ProfileRevision = 8 });
        store.Write(Manifest(Keys.Target("client"), "C:\\client", []) with { ProfileId = profile, ProfileRevision = 8 });

        var agreed = store.TryReadAgreed([Keys.Target("server"), Keys.Target("client")]);

        Assert.Equal(8, agreed?.ProfileRevision);
    }

    /// <summary>
    /// One folder did not get an apply the other did, so there is no revision this game is on -
    /// and attributing an evening to either number would name a mod list the save may never have run
    /// on.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(null)]
    public void Targets_that_disagree_answer_with_nothing(int? clientRevision)
    {
        using var directory = new TempDirectory("manifests-disagree");
        var store = new SyncManifestStore(directory.Path);
        var profile = Guid.NewGuid();

        store.Write(Manifest(Keys.Target("server"), "C:\\server", []) with { ProfileId = profile, ProfileRevision = 8 });

        if (clientRevision is int revision)
        {
            store.Write(Manifest(Keys.Target("client"), "C:\\client", []) with { ProfileId = profile, ProfileRevision = revision });
        }

        // Null covers the other half: a target that has never been applied to is not agreement, it
        // is one folder with no answer at all.
        Assert.Null(store.TryReadAgreed([Keys.Target("server"), Keys.Target("client")]));
    }

    /// <summary>A game whose settings point at no folder has nothing to agree about.</summary>
    [Fact]
    public void A_game_reaching_no_folder_agrees_on_nothing()
    {
        using var directory = new TempDirectory("manifests-none");

        Assert.Null(new SyncManifestStore(directory.Path).TryReadAgreed([]));
    }


    private static string Name(ModTargetRef target) => StoreFileName.For(target.Game.ToString(), target.Key.Value);

    private static SyncManifest Manifest(ModTargetRef target, string modFolder, IReadOnlyList<SyncManifestEntry> entries)
    {
        return new SyncManifest
        {
            Target = target,
            RepoId = Guid.NewGuid(),
            ProfileId = Guid.NewGuid(),
            SyncedAt = DateTimeOffset.UtcNow,
            ModFolder = modFolder,
            Entries = entries
        };
    }
}
