using ModsDude.Client.Core.Disks;
using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Tests.Disks;

public class DiskSpaceGuardTests
{
    private const long _reserve = DiskSpaceGuard.ReserveBytes;

    private static readonly string _path = Path.Combine(Path.GetTempPath(), "modsdude-tests", "disk-space");
    private static readonly string _volume = FileSystemHelper.NormalizeVolumeRoot(_path);


    [Fact]
    public void Work_that_leaves_the_reserve_free_goes_ahead()
    {
        var disks = new FakeDiskSpace();
        disks.Set(_volume, 500 + _reserve);

        new DiskSpaceGuard(disks).EnsureRoomFor(DiskSpaceNeeds.For(_path, 500));
    }

    [Fact]
    public void Work_that_would_eat_into_the_reserve_is_refused_with_what_to_free()
    {
        var disks = new FakeDiskSpace();
        disks.Set(_volume, 500 + _reserve - 1);

        var exception = Assert.Throws<NotEnoughDiskSpaceException>(
            () => new DiskSpaceGuard(disks).EnsureRoomFor(DiskSpaceNeeds.For(_path, 500)));

        Assert.Equal([new DiskShortfall(_volume, 1)], exception.Shortfalls);
        Assert.Contains(_volume, exception.UserMessage);
    }

    [Fact]
    public void Writes_to_one_volume_add_up()
    {
        var disks = new FakeDiskSpace();
        disks.Set(_volume, 500 + _reserve);

        var needs = new DiskSpaceNeeds();
        needs.Add(Path.Combine(_path, "store"), 300);
        needs.Add(Path.Combine(_path, "mods"), 201);

        var exception = Assert.Throws<NotEnoughDiskSpaceException>(() => new DiskSpaceGuard(disks).EnsureRoomFor(needs));

        Assert.Equal(1, Assert.Single(exception.Shortfalls).MissingBytes);
    }

    [Fact]
    public void Work_that_writes_nothing_is_never_refused_or_measured()
    {
        var disks = new FakeDiskSpace { Available = 0 };

        new DiskSpaceGuard(disks).EnsureRoomFor(DiskSpaceNeeds.For(_path, 0));

        Assert.Empty(disks.Asked);
    }

    [Fact]
    public void A_volume_whose_free_space_cannot_be_read_does_not_refuse()
    {
        var disks = new FakeDiskSpace { Available = null };

        new DiskSpaceGuard(disks).EnsureRoomFor(DiskSpaceNeeds.For(_path, 500));

        Assert.Equal([_volume], disks.Asked);
    }

    [Fact]
    public void Every_volume_short_of_room_is_named_in_order()
    {
        var disks = new FakeDiskSpace();
        disks.Set("C:\\", _reserve);
        disks.Set("D:\\", _reserve);
        disks.Set("E:\\", 10 + _reserve);

        var needs = new DiskSpaceNeeds();
        needs.Add("E:\\Mods", 10);
        needs.Add("D:\\Store", 20);
        needs.Add("C:\\Games\\Mods", 30);

        var exception = Assert.Throws<NotEnoughDiskSpaceException>(() => new DiskSpaceGuard(disks).EnsureRoomFor(needs));

        Assert.Equal([new DiskShortfall("C:\\", 30), new DiskShortfall("D:\\", 20)], exception.Shortfalls);
    }
}
