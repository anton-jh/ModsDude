using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Tests.Helpers;

/// <summary>
/// What a store is allowed to put in a file name, given that two of the strings in one were written
/// by an adapter author this code has never seen.
/// </summary>
public class StoreFileNameTests
{
    /// <summary>
    /// The name the phase spends an argument on. A manifest anybody opens the folder for should say
    /// which game and which target it is.
    /// </summary>
    [Fact]
    public void An_ordinary_identity_and_target_stay_readable()
    {
        Assert.Equal("_farming_simulator#fs25_mods", StoreFileName.For("_farming_simulator#fs25", "mods"));
    }

    /// <summary>
    /// Manifests already on disk keep the names they have. A Guid is hex and dashes, so it passes
    /// through untouched and routing the existing store through the encoder changes nothing.
    /// </summary>
    [Fact]
    public void A_guid_is_left_exactly_as_it_was()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id.ToString(), StoreFileName.For(id.ToString()));
    }

    /// <summary>
    /// The point of encoding rather than refusing: an adapter that names a target after a path still
    /// gets a manifest, instead of finding out at sync time on somebody else's machine.
    /// </summary>
    [Theory]
    [InlineData(@"a/b", "a%2Fb")]
    [InlineData(@"a\b", "a%5Cb")]
    [InlineData("a:b", "a%3Ab")]
    [InlineData("a*b", "a%2Ab")]
    [InlineData("a?b", "a%3Fb")]
    [InlineData("a|b", "a%7Cb")]
    [InlineData("a b", "a%20b")]
    [InlineData("a\"b", "a%22b")]
    public void A_character_a_filesystem_refuses_is_escaped(string part, string expected)
    {
        Assert.Equal(expected, StoreFileName.For(part));
    }

    /// <summary>Or the encoding would have no escape of its own, and two parts could collide.</summary>
    [Fact]
    public void The_escape_character_escapes_itself()
    {
        Assert.Equal("a%25b", StoreFileName.For("a%b"));
        Assert.NotEqual(StoreFileName.For("a%2Fb"), StoreFileName.For("a/b"));
    }

    [Fact]
    public void Something_outside_ascii_is_escaped_as_its_utf8_bytes()
    {
        Assert.Equal("f%C3%A4rgspel", StoreFileName.For("färgspel"));
    }

    /// <summary>
    /// Reserved with any extension on it, so the guard is on the name rather than the file: opening
    /// <c>CON.json</c> opens the console.
    /// </summary>
    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("COM1")]
    [InlineData("LPT9.json")]
    public void A_device_name_stops_being_one(string part)
    {
        var name = StoreFileName.For(part);

        Assert.StartsWith("%", name);
        Assert.NotEqual(part, name);
    }

    /// <summary>Windows strips it on the way to the filesystem, so two names would be one file.</summary>
    [Fact]
    public void A_trailing_dot_is_escaped()
    {
        Assert.Equal("mods%2E", StoreFileName.For("mods."));
        Assert.NotEqual(StoreFileName.For("mods."), StoreFileName.For("mods"));
    }

    /// <summary>
    /// A path component is capped at 255 characters, and a name that overran it would be a manifest
    /// that could not be written at all.
    /// </summary>
    [Fact]
    public void A_pathological_length_is_capped()
    {
        var name = StoreFileName.For(new string('a', 400));

        Assert.True(name.Length <= 120, $"Expected at most 120 characters, got {name.Length}.");
    }

    /// <summary>
    /// And the cap is a truncation with a stamp rather than a truncation, so two targets agreeing for
    /// their first hundred characters stay two manifests instead of overwriting each other.
    /// </summary>
    [Fact]
    public void Two_long_names_that_agree_at_the_front_stay_two_names()
    {
        var prefix = new string('a', 400);

        Assert.NotEqual(StoreFileName.For($"{prefix}one"), StoreFileName.For($"{prefix}two"));
    }

    /// <summary>
    /// A cap landing inside a '%2F' would leave a '%2' that reads as a literal percent and a two,
    /// which is a name saying something untrue about what was encoded.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("aa")]
    public void A_cap_never_lands_inside_an_escape(string prefix)
    {
        // Each of these escapes to three characters, so the prefix is what decides whether the cap
        // falls between two of them or in the middle of one.
        var name = StoreFileName.For(prefix + new string('/', 100));
        var stem = name[..name.LastIndexOf('-')];

        Assert.EndsWith("%2F", stem);
    }
}
