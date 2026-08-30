using ModsDude.Client.Core.Sync;
using System.Text;

namespace ModsDude.Client.Core.Tests.Sync;

public class FileLinksTests
{
    [Fact]
    public void A_new_file_has_one_name()
    {
        using var directory = new TempDirectory("links-count");
        var path = directory.WriteFile("blob", "bytes");

        Assert.Equal(1, FileLinks.TryGetLinkCount(path));
    }

    [Fact]
    public void A_hardlink_is_a_second_name_for_the_same_data()
    {
        using var directory = new TempDirectory("links-same-volume");
        var blob = directory.WriteFile("blob", "bytes");
        var link = directory.Combine("installed.zip");

        Assert.True(
            FileLinks.TryCreateHardLink(link, blob),
            $"Could not create a hardlink under '{directory.Path}'. Hardlinks need NTFS; on a filesystem " +
            "without them the store falls back to copying, but that fallback cannot be told apart from a " +
            "bug here.");

        Assert.Equal(2, FileLinks.TryGetLinkCount(blob));
        Assert.Equal("bytes", File.ReadAllText(link));

        // Deleting one name leaves the other, and the data, untouched. That is what makes
        // uninstalling from a hardlink-served disk free.
        File.Delete(link);

        Assert.Equal(1, FileLinks.TryGetLinkCount(blob));
        Assert.Equal("bytes", File.ReadAllText(blob));
    }

    /// <summary>
    /// The reason a store is per volume rather than per machine, and the reason a cross-disk
    /// assignment materialises by copy: a hardlink cannot cross one.
    /// </summary>
    [Fact]
    public void A_hardlink_cannot_cross_a_volume()
    {
        using var here = new TempDirectory("links-cross-volume");
        var blob = here.WriteFile("blob", "bytes");

        // Always checkable: refusing is an ordinary answer here rather than an exception, because the
        // caller's response to any refusal is the same - copy instead.
        Assert.False(FileLinks.TryCreateHardLink(Path.Combine(here.Path, "no", "such", "folder", "link"), blob));

        if (FindOtherVolume(here.Path) is not string other)
        {
            // A machine with one usable fixed volume cannot exercise the real cross-volume refusal.
            // The check above still holds; this half is simply unverified here.
            return;
        }

        var directory = Path.Combine(other, "modsdude-tests", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(directory);

            var link = Path.Combine(directory, "installed.zip");

            Assert.False(FileLinks.TryCreateHardLink(link, blob));
            Assert.False(File.Exists(link));

            // What the copy-served path does instead, and it has to work across the volume boundary.
            File.Copy(blob, link);

            Assert.Equal("bytes", File.ReadAllText(link));
            Assert.Equal(1, FileLinks.TryGetLinkCount(blob));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception)
            {
                // Cleanup only.
            }
        }
    }

    [Fact]
    public void The_link_count_of_a_file_that_is_not_there_is_unknown()
    {
        using var directory = new TempDirectory("links-missing");

        // Null rather than one: callers treat it as "assume it is shared", so an unreadable file is
        // never evicted on a guess.
        Assert.Null(FileLinks.TryGetLinkCount(directory.Combine("nothing")));
    }


    private static string? FindOtherVolume(string path)
    {
        var here = Path.GetPathRoot(Path.GetFullPath(path));

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not DriveType.Fixed || drive.IsReady is false)
            {
                continue;
            }

            var root = drive.RootDirectory.FullName;

            if (string.Equals(root, here, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsWritable(root))
            {
                return root;
            }
        }

        return null;
    }

    private static bool IsWritable(string root)
    {
        var probe = Path.Combine(root, $"modsdude-write-probe-{Guid.NewGuid():N}");

        try
        {
            File.WriteAllBytes(probe, Encoding.UTF8.GetBytes("probe"));
            File.Delete(probe);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
