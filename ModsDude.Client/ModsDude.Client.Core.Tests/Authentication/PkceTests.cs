using ModsDude.Client.Core.Authentication;

namespace ModsDude.Client.Core.Tests.Authentication;

public class PkceTests
{
    [Fact]
    public void The_challenge_matches_the_example_in_RFC_7636()
    {
        Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            Pkce.ChallengeFor("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void A_verifier_is_43_unreserved_characters()
    {
        var verifier = Pkce.CreateVerifier();

        Assert.Equal(43, verifier.Length);
        Assert.All(verifier, x => Assert.True(char.IsAsciiLetterOrDigit(x) || x is '-' or '_'));
    }

    [Fact]
    public void Verifiers_differ()
    {
        Assert.NotEqual(Pkce.CreateVerifier(), Pkce.CreateVerifier());
    }
}
