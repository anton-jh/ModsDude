using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Domain.Tests.Storage;

public class StorageBindingTests
{
    private static readonly Guid _ours = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid _theirs = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid _advance = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid _otherAdvance = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");


    [Fact]
    public void A_marker_round_trips()
    {
        var marker = new ContainerMarker(_ours, 7, _advance);

        Assert.Equal(marker, ContainerMarker.Parse(marker.Format()));
    }

    [Fact]
    public void A_marker_written_before_the_first_advance_round_trips()
    {
        var marker = new ContainerMarker(_ours, 0);

        Assert.Equal(marker, ContainerMarker.Parse(marker.Format()));
    }

    [Fact]
    public void A_marker_holding_only_an_id_reads_as_having_no_generation()
    {
        Assert.Equal(new ContainerMarker(_ours, null), ContainerMarker.Parse(_ours.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("{\"BindingId\":\"00000000-0000-0000-0000-000000000000\",\"Generation\":1}")]
    [InlineData("{\"BindingId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"Generation\":-1}")]
    [InlineData("{\"BindingId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"Generation\":1,\"AdvanceId\":\"00000000-0000-0000-0000-000000000000\"}")]
    public void A_marker_that_is_not_one_is_refused(string text)
    {
        Assert.Throws<FormatException>(() => ContainerMarker.Parse(text));
    }

    [Fact]
    public void A_marker_at_the_binding_and_its_generation_is_bound()
    {
        Assert.Equal(ContainerBinding.Bound, Binding().Judge(new ContainerMarker(_ours, 0)));
    }

    [Fact]
    public void No_marker_is_unbound_and_another_binding_is_foreign()
    {
        Assert.Equal(ContainerBinding.Unbound, Binding().Judge(null));
        Assert.Equal(ContainerBinding.Foreign, Binding().Judge(new ContainerMarker(_theirs, 0)));
    }

    [Fact]
    public void A_marker_ahead_of_the_database_is_stale()
    {
        // A database restored from a backup taken before the last run.
        Assert.Equal(ContainerBinding.Stale, Binding().Judge(new ContainerMarker(_ours, 3)));
    }

    [Fact]
    public void A_marker_behind_the_database_is_stale()
    {
        var binding = AdvancedTo(2);

        Assert.Equal(ContainerBinding.Stale, binding.Judge(new ContainerMarker(_ours, 1)));
    }

    [Fact]
    public void A_marker_without_a_generation_is_upgraded_only_against_a_binding_that_never_advanced()
    {
        Assert.Equal(ContainerBinding.Legacy, Binding().Judge(new ContainerMarker(_ours, null)));
        Assert.Equal(ContainerBinding.Stale, AdvancedTo(1).Judge(new ContainerMarker(_ours, null)));
    }

    [Fact]
    public void Between_the_phases_of_an_advance_both_generations_are_accepted()
    {
        var binding = Binding();
        binding.BeginAdvance(_advance);

        Assert.Equal(ContainerBinding.Bound, binding.Judge(new ContainerMarker(_ours, 0)));
        Assert.Equal(ContainerBinding.Bound, binding.Judge(new ContainerMarker(_ours, 1, _advance)));
        Assert.Equal(new ContainerMarker(_ours, 1, _advance), binding.Marker);
    }

    [Fact]
    public void An_advance_begun_again_after_a_crash_is_the_same_advance()
    {
        var binding = Binding();
        binding.BeginAdvance(_advance);
        binding.BeginAdvance(_otherAdvance);
        binding.CompleteAdvance();

        Assert.Equal(1, binding.Generation);
        Assert.Null(binding.PendingGeneration);
        Assert.Equal(new ContainerMarker(_ours, 1, _advance), binding.Marker);
        Assert.Equal(ContainerBinding.Stale, binding.Judge(new ContainerMarker(_ours, 0)));
    }

    [Fact]
    public void Two_copies_advancing_at_once_refuse_each_others_markers()
    {
        var copy = Binding();
        var other = Binding();

        copy.BeginAdvance(_advance);
        other.BeginAdvance(_otherAdvance);

        Assert.Equal(ContainerBinding.Stale, copy.Judge(other.Marker));
        Assert.Equal(ContainerBinding.Stale, other.Judge(copy.Marker));
    }

    [Fact]
    public void After_an_advance_only_its_own_markers_are_accepted()
    {
        var binding = Binding();
        binding.BeginAdvance(_advance);
        binding.CompleteAdvance();

        Assert.Equal(ContainerBinding.Bound, binding.Judge(new ContainerMarker(_ours, 1, _advance)));
        Assert.Equal(ContainerBinding.Stale, binding.Judge(new ContainerMarker(_ours, 1, _otherAdvance)));
    }

    [Fact]
    public void Completing_an_advance_that_never_began_is_a_bug()
    {
        Assert.Throws<InvalidOperationException>(() => Binding().CompleteAdvance());
    }

    [Fact]
    public void Binding_by_hand_moves_past_every_marker_seen()
    {
        var binding = AdvancedTo(2);

        binding.Rebind("other.blob.core.windows.net", 9, _otherAdvance);

        Assert.Equal(10, binding.Generation);
        Assert.Equal(new ContainerMarker(_ours, 10, _otherAdvance), binding.Marker);
        Assert.Equal("other.blob.core.windows.net", binding.StorageAccount);
        Assert.Equal(ContainerBinding.Stale, binding.Judge(new ContainerMarker(_ours, 9)));
    }

    [Fact]
    public void Binding_by_hand_moves_on_even_when_no_marker_has_a_generation()
    {
        var binding = AdvancedTo(4);

        binding.Rebind("account", null, _otherAdvance);

        Assert.Equal(5, binding.Generation);
    }

    [Fact]
    public void Binding_by_hand_mid_advance_moves_past_the_pending_generation()
    {
        var binding = AdvancedTo(2);
        binding.BeginAdvance(_advance);

        binding.Rebind("account", null, _otherAdvance);

        Assert.Equal(4, binding.Generation);
        Assert.Null(binding.PendingGeneration);
        Assert.Null(binding.PendingAdvanceId);
        Assert.Equal(ContainerBinding.Stale, binding.Judge(new ContainerMarker(_ours, 3, _advance)));
    }


    private static StorageBinding Binding()
    {
        return StorageBinding.Create(_ours, DateTime.UnixEpoch, "account");
    }

    private static StorageBinding AdvancedTo(int generation)
    {
        var binding = Binding();

        for (var i = 0; i < generation; i++)
        {
            binding.BeginAdvance(new Guid(i + 1, 0, 0, new byte[8]));
            binding.CompleteAdvance();
        }

        return binding;
    }
}
