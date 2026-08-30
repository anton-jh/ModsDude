using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Users;

public class UsernameAllocatorTests
{
    [Fact]
    public void A_name_nobody_holds_is_stored_exactly_as_the_provider_gave_it()
    {
        Assert.Equal(new Username("Anton"), Resolve("Anton", taken: []));
    }

    [Fact]
    public void A_second_user_with_the_same_display_name_gets_a_suffix()
    {
        // The whole point: before this, the second Anton could never complete a single request.
        Assert.Equal(new Username("Anton (2)"), Resolve("Anton", taken: ["Anton"]));
    }

    [Fact]
    public void Each_further_collision_takes_the_next_suffix()
    {
        Assert.Equal(new Username("Anton (4)"), Resolve("Anton", taken: ["Anton", "Anton (2)", "Anton (3)"]));
    }

    /// <summary>
    /// Someone whose display name really is "Anton (2)" must not be handed a name that is free only
    /// because the numbering happened to skip it, and must not be pushed off their own name either.
    /// </summary>
    [Fact]
    public void A_display_name_that_already_looks_suffixed_is_resolved_on_its_own_terms()
    {
        Assert.Equal(new Username("Anton (2)"), Resolve("Anton (2)", taken: ["Anton"]));
        Assert.Equal(new Username("Anton (2) (2)"), Resolve("Anton (2)", taken: ["Anton", "Anton (2)"]));
    }

    [Fact]
    public void The_answer_depends_only_on_the_desired_name_and_the_names_already_held()
    {
        // Determinism is what makes a retried first request converge instead of minting a new name
        // every time it is attempted.
        var first = Resolve("Anton", taken: ["Anton", "Anton (2)"]);
        var second = Resolve("Anton", taken: ["Anton (2)", "Anton"]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_gap_in_the_numbering_is_filled_rather_than_stepped_over()
    {
        Assert.Equal(new Username("Anton (3)"), Resolve("Anton", taken: ["Anton", "Anton (2)", "Anton (4)"]));
    }

    [Fact]
    public void Candidates_start_at_the_desired_name_and_are_all_distinct()
    {
        var candidates = UsernameAllocator.GetCandidates(new Username("Anton")).ToList();

        Assert.Equal(new Username("Anton"), candidates[0]);
        Assert.Equal(UsernameAllocator.MaximumCandidates, candidates.Count);
        Assert.Equal(candidates.Count, candidates.Distinct().Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_or_blank_name_claim_falls_back_rather_than_failing(string? claim)
    {
        // A claim the provider did not send used to throw, which locked the user out exactly as a
        // collision did. The fallback then collides like any other name and is disambiguated the
        // same way.
        Assert.Equal(new Username(UsernameAllocator.FallbackDisplayName), UsernameAllocator.FromDisplayName(claim));
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_so_that_two_spellings_are_not_two_names()
    {
        Assert.Equal(new Username("Anton"), UsernameAllocator.FromDisplayName("  Anton  "));
    }


    /// <summary>
    /// What the provisioning middleware does: walk the candidates and take the first name nobody
    /// holds.
    /// </summary>
    private static Username Resolve(string desired, string[] taken)
    {
        var held = taken.Select(x => new Username(x)).ToHashSet();

        return UsernameAllocator
            .GetCandidates(UsernameAllocator.FromDisplayName(desired))
            .First(x => !held.Contains(x));
    }
}
