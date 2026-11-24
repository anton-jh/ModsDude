using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Domain.Tests.Storage;

/// <summary>
/// The gate must never let a database that is not storage's own write to it or delete from it, and
/// must never let anything be deleted that storage cannot give back.
/// </summary>
public class StorageGateTests
{
    private const string _account = "modsdude.blob.core.windows.net";
    private const int _minimumDays = 14;

    private static readonly Guid _ours = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid _theirs = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");


    [Fact]
    public void Containers_marked_at_this_bindings_generation_may_be_written_and_deleted_from()
    {
        var decision = Decide(Binding(), All(new ContainerMarker(_ours, 0), isEmpty: false));

        Assert.True(decision.MayDelete);
        Assert.Empty(decision.ToMark);
        Assert.Empty(decision.ToUpgrade);
        Assert.False(decision.CreateBinding);
    }

    [Fact]
    public void A_database_with_no_binding_binds_entirely_empty_storage()
    {
        var decision = Decide(null, All(null, isEmpty: true));

        Assert.True(decision.MayWrite);
        Assert.True(decision.CreateBinding);
        Assert.Equal(StorageContainerNames.All, decision.ToMark);
    }

    [Fact]
    public void A_database_with_no_binding_never_binds_storage_that_holds_anything()
    {
        var decision = Decide(null,
        [
            new(StorageContainer.Mods, null, false),
            new(StorageContainer.Savegames, null, true),
            new(StorageContainer.Images, null, true)
        ]);

        Assert.False(decision.MayWrite);
        Assert.False(decision.CreateBinding);
        Assert.Empty(decision.ToMark);
    }

    [Fact]
    public void A_database_with_no_binding_against_marked_storage_is_refused()
    {
        // A reset database, pointed at storage another database bound.
        var decision = Decide(null, All(new ContainerMarker(_theirs, 4), isEmpty: true));

        Assert.False(decision.MayWrite);
        Assert.Equal(3, decision.WriteRefusals.Count);
    }

    [Fact]
    public void One_container_bound_to_another_database_refuses_everything()
    {
        var decision = Decide(Binding(),
        [
            new(StorageContainer.Mods, new ContainerMarker(_ours, 0), false),
            new(StorageContainer.Savegames, new ContainerMarker(_theirs, 0), false),
            new(StorageContainer.Images, new ContainerMarker(_ours, 0), false)
        ]);

        Assert.False(decision.MayWrite);
        Assert.False(decision.MayDelete);
        Assert.Contains("'savegames' is bound to another database.", decision.WriteRefusals);
    }

    [Fact]
    public void A_marker_at_another_generation_refuses_everything()
    {
        // A database restored from a backup: the right binding, but behind the markers.
        var decision = Decide(Binding(), All(new ContainerMarker(_ours, 5), isEmpty: false));

        Assert.False(decision.MayWrite);
        Assert.Equal(3, decision.WriteRefusals.Count);
    }

    [Fact]
    public void An_unmarked_container_that_holds_blobs_is_refused()
    {
        var decision = Decide(Binding(),
        [
            new(StorageContainer.Mods, new ContainerMarker(_ours, 0), false),
            new(StorageContainer.Savegames, null, false),
            new(StorageContainer.Images, new ContainerMarker(_ours, 0), false)
        ]);

        Assert.False(decision.MayWrite);
        Assert.Contains("'savegames' has no marker and is not empty.", decision.WriteRefusals);
    }

    [Fact]
    public void An_unmarked_empty_container_is_marked()
    {
        var decision = Decide(Binding(),
        [
            new(StorageContainer.Mods, new ContainerMarker(_ours, 0), false),
            new(StorageContainer.Savegames, null, true),
            new(StorageContainer.Images, new ContainerMarker(_ours, 0), false)
        ]);

        Assert.True(decision.MayDelete);
        Assert.Equal([StorageContainer.Savegames], decision.ToMark);
    }

    [Fact]
    public void Markers_from_before_generations_are_upgraded()
    {
        var decision = Decide(Binding(), All(new ContainerMarker(_ours, null), isEmpty: false));

        Assert.True(decision.MayDelete);
        Assert.Equal(StorageContainerNames.All, decision.ToUpgrade);
    }

    [Fact]
    public void Another_storage_account_is_refused()
    {
        var decision = Decide(Binding(), All(new ContainerMarker(_ours, 0), isEmpty: false), account: "copy.blob.core.windows.net");

        Assert.False(decision.MayWrite);
    }

    [Fact]
    public void A_binding_with_no_account_recorded_records_this_one()
    {
        var binding = StorageBinding.Create(_ours, DateTime.UnixEpoch, null!);

        var decision = Decide(binding, All(new ContainerMarker(_ours, null), isEmpty: false));

        Assert.True(decision.MayWrite);
        Assert.True(decision.RecordStorageAccount);
    }

    [Fact]
    public void An_unreadable_marker_refuses_everything()
    {
        var decision = Decide(Binding(),
        [
            new(StorageContainer.Mods, new ContainerMarker(_ours, 0), false),
            new(StorageContainer.Savegames, null, false, MarkerIsUnreadable: true),
            new(StorageContainer.Images, new ContainerMarker(_ours, 0), false)
        ]);

        Assert.False(decision.MayWrite);
        Assert.Equal(["'savegames' has a marker that can't be read."], decision.WriteRefusals);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(13)]
    public void Soft_delete_that_is_off_short_or_unreadable_stops_deletes_but_not_writes(int? days)
    {
        var decision = Decide(Binding(), All(new ContainerMarker(_ours, 0), isEmpty: false), softDeleteDays: days);

        Assert.True(decision.MayWrite);
        Assert.False(decision.MayDelete);
        Assert.Single(decision.DeleteRefusals);
    }

    [Fact]
    public void Soft_delete_of_exactly_the_minimum_is_enough()
    {
        var decision = Decide(Binding(), All(new ContainerMarker(_ours, 0), isEmpty: false), softDeleteDays: _minimumDays);

        Assert.True(decision.MayDelete);
    }

    [Fact]
    public void A_refused_gate_asks_for_no_writes()
    {
        var decision = Decide(Binding(),
        [
            new(StorageContainer.Mods, new ContainerMarker(_ours, null), false),
            new(StorageContainer.Savegames, null, true),
            new(StorageContainer.Images, new ContainerMarker(_theirs, 0), false)
        ]);

        Assert.False(decision.MayWrite);
        Assert.Empty(decision.ToMark);
        Assert.Empty(decision.ToUpgrade);
        Assert.False(decision.RecordStorageAccount);
    }


    private static StorageBinding Binding()
    {
        return StorageBinding.Create(_ours, DateTime.UnixEpoch, _account);
    }

    private static IReadOnlyList<ContainerState> All(ContainerMarker? marker, bool isEmpty)
    {
        return [.. StorageContainerNames.All.Select(x => new ContainerState(x, marker, isEmpty))];
    }

    private static StorageGateDecision Decide(
        StorageBinding? binding,
        IReadOnlyList<ContainerState> containers,
        string account = _account,
        int? softDeleteDays = 31)
    {
        return StorageGate.Decide(new StorageGateInput(binding, containers, account, softDeleteDays, _minimumDays));
    }
}
