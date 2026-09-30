using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameFiles;
using ModsDude.Client.Core.Tests.Sync;
using System.Text;

namespace ModsDude.Client.Core.Tests.GameFiles;

public class GameFileEditorTests : IDisposable
{
    private readonly TempDirectory _folder = new("game-file-editor");
    private readonly FakeRecycleBin _recycleBin = new();


    public void Dispose() => _folder.Dispose();


    [Fact]
    public void Applying_writes_what_the_transform_returns()
    {
        _folder.WriteFile("config.txt", "old");

        var changed = Editor().Apply(_folder.Path, Replace("config.txt", "new"));

        Assert.True(changed);
        Assert.Equal("new", File.ReadAllText(_folder.Combine("config.txt")));
    }

    [Fact]
    public void Applying_creates_a_file_that_does_not_exist_yet()
    {
        Assert.True(Editor().Apply(_folder.Path, Replace("config.txt", "new")));
        Assert.Equal("new", File.ReadAllText(_folder.Combine("config.txt")));
    }

    [Fact]
    public void Applying_the_same_edit_again_changes_nothing()
    {
        _folder.WriteFile("config.txt", "old");
        var edit = Replace("config.txt", "new");
        Editor().Apply(_folder.Path, edit);
        var written = File.GetLastWriteTimeUtc(_folder.Combine("config.txt"));

        Assert.False(Editor().Apply(_folder.Path, edit));
        Assert.Equal(written, File.GetLastWriteTimeUtc(_folder.Combine("config.txt")));
    }

    [Fact]
    public void A_transform_returning_null_leaves_the_file_alone()
    {
        Assert.False(Editor().Apply(_folder.Path, new GameFileEdit("config.txt", _ => null)));
        Assert.False(File.Exists(_folder.Combine("config.txt")));
    }

    [Fact]
    public void The_transform_is_given_the_current_content()
    {
        _folder.WriteFile("config.txt", "kept");

        Editor().Apply(_folder.Path, new GameFileEdit("config.txt", current => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(current!) + " + added")));

        Assert.Equal("kept + added", File.ReadAllText(_folder.Combine("config.txt")));
    }

    [Fact]
    public void Replaced_content_goes_to_the_recycle_bin_when_the_edit_asks_for_it()
    {
        _folder.WriteFile("config.txt", "old");

        Editor().Apply(_folder.Path, Replace("config.txt", "new") with { RecycleReplaced = true });

        Assert.Equal("old", Assert.Single(_recycleBin.Recycled));
        Assert.Equal("new", File.ReadAllText(_folder.Combine("config.txt")));
    }

    [Fact]
    public void Replaced_content_is_not_recycled_unless_the_edit_asks()
    {
        _folder.WriteFile("config.txt", "old");

        Editor().Apply(_folder.Path, Replace("config.txt", "new"));

        Assert.Empty(_recycleBin.Recycled);
    }

    [Fact]
    public void A_file_whose_old_content_cannot_be_recycled_is_left_unchanged()
    {
        _folder.WriteFile("config.txt", "old");
        var editor = new GameFileEditor(new FakeRecycleBin(available: false), NullLogger<GameFileEditor>.Instance);

        Assert.Throws<IOException>(() => editor.Apply(_folder.Path, Replace("config.txt", "new") with { RecycleReplaced = true }));
        Assert.Equal("old", File.ReadAllText(_folder.Combine("config.txt")));
    }

    [Fact]
    public void A_path_may_climb_out_of_the_folder_it_is_relative_to()
    {
        var inner = _folder.CreateSubdirectory("mods");

        Editor().Apply(inner, Replace(Path.Combine("..", "db.json"), "new"));

        Assert.Equal("new", File.ReadAllText(_folder.Combine("db.json")));
    }

    [Fact]
    public void A_rooted_path_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Editor().Apply(_folder.Path, Replace(_folder.Combine("config.txt"), "new")));
    }

    [Fact]
    public void Planning_says_whether_the_file_would_change_without_writing()
    {
        _folder.WriteFile("config.txt", "old");

        var changing = Editor().Plan(_folder.Path, Replace("config.txt", "new"));
        var same = Editor().Plan(_folder.Path, Replace("config.txt", "old"));

        Assert.True(changing.Changes);
        Assert.False(same.Changes);
        Assert.Equal("old", File.ReadAllText(_folder.Combine("config.txt")));
    }


    private GameFileEditor Editor() => new(_recycleBin, NullLogger<GameFileEditor>.Instance);

    private static GameFileEdit Replace(string relativePath, string content)
        => new(relativePath, _ => Encoding.UTF8.GetBytes(content));
}
