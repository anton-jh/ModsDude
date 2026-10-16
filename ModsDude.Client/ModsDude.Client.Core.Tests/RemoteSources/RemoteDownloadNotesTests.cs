using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.RemoteSources;
using ModsDude.Client.Core.Tests.Sync;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.RemoteSources;

public class RemoteDownloadNotesTests : IDisposable
{
    private readonly TempDirectory _temp = new("remote-notes");


    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }


    [Fact]
    public void A_note_is_found_again_after_a_restart()
    {
        var note = Note(1, held: "1.2");

        new RemoteDownloadNotes(Path).Record(note);

        Assert.Equal(note, new RemoteDownloadNotes(Path).Find(note.Url));
    }

    [Fact]
    public void A_download_that_was_not_a_mod_is_remembered_as_holding_nothing()
    {
        var note = Note(1, held: null);

        new RemoteDownloadNotes(Path).Record(note);

        var found = new RemoteDownloadNotes(Path).Find(note.Url);

        Assert.NotNull(found);
        Assert.Null(found.Held);
    }

    [Fact]
    public void Recording_a_link_again_replaces_what_it_held()
    {
        var notes = new RemoteDownloadNotes(Path);

        notes.Record(Note(1, held: "1.2"));
        notes.Record(Note(1, held: "1.1"));

        Assert.Equal(V("1.1"), new RemoteDownloadNotes(Path).Find(Note(1, null).Url)!.Held!.Value.VersionId);
    }

    [Fact]
    public void Only_the_newest_notes_are_kept()
    {
        var notes = new RemoteDownloadNotes(Path);

        for (var i = 0; i < RemoteDownloadNotes.Capacity + 5; i++)
        {
            notes.Record(Note(i, held: "1.1"));
        }

        var reread = new RemoteDownloadNotes(Path);

        Assert.Null(reread.Find(Note(4, null).Url));
        Assert.NotNull(reread.Find(Note(5, null).Url));
        Assert.NotNull(reread.Find(Note(RemoteDownloadNotes.Capacity + 4, null).Url));
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_started_over()
    {
        File.WriteAllText(Path, "{ not json");

        var notes = new RemoteDownloadNotes(Path);

        Assert.Null(notes.Find(Note(1, null).Url));

        notes.Record(Note(1, held: "1.1"));

        Assert.NotNull(new RemoteDownloadNotes(Path).Find(Note(1, null).Url));
    }

    [Fact]
    public void An_entry_that_does_not_make_sense_is_skipped_rather_than_failing_every_lookup()
    {
        File.WriteAllText(Path, """
            [
              { "Url": "https://cdn.example.com/a.zip", "OfferedMod": "", "OfferedVersion": "1.0", "HeldMod": null, "HeldVersion": null },
              { "Url": "https://cdn.example.com/b.zip", "OfferedMod": "b", "OfferedVersion": "1.0", "HeldMod": "b", "HeldVersion": "1.1" }
            ]
            """);

        var notes = new RemoteDownloadNotes(Path);

        Assert.Null(notes.Find(new Uri("https://cdn.example.com/a.zip")));
        Assert.NotNull(notes.Find(new Uri("https://cdn.example.com/b.zip")));
    }


    private string Path => _temp.Combine("remote-downloads.json");

    private static RemoteDownloadNote Note(int link, string? held)
        => new(
            new Uri($"https://cdn.example.com/{link}/FS25_Plough.zip"),
            new ModVersionIdentity(Mod("FS25_Plough"), V("1.1")),
            held is null ? null : new ModVersionIdentity(Mod("FS25_Plough"), V(held)));
}
