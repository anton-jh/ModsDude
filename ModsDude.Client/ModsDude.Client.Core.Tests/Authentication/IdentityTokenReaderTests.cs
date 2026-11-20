using ModsDude.Client.Core.Authentication;

namespace ModsDude.Client.Core.Tests.Authentication;

public class IdentityTokenReaderTests
{
    [Fact]
    public void Reads_the_subject_and_name()
    {
        var identity = IdentityTokenReader.Read(TestTokens.IdentityToken(new { sub = "user-1", name = "Anton" }));

        Assert.Equal(new SignedInIdentity("user-1", "Anton"), identity);
    }

    [Fact]
    public void A_missing_name_reads_as_empty()
    {
        var identity = IdentityTokenReader.Read(TestTokens.IdentityToken(new { sub = "user-1" }));

        Assert.Equal(new SignedInIdentity("user-1", ""), identity);
    }

    [Fact]
    public void A_token_without_a_subject_is_refused()
    {
        Assert.Throws<FormatException>(() => IdentityTokenReader.Read(TestTokens.IdentityToken(new { name = "Anton" })));
    }

    [Fact]
    public void Something_that_is_not_a_JWT_is_refused()
    {
        Assert.Throws<FormatException>(() => IdentityTokenReader.Read("not-a-token"));
    }
}
