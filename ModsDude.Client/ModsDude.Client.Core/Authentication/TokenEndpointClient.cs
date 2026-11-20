using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModsDude.Client.Core.Authentication;

/// <summary>The server's token endpoint.</summary>
public sealed class TokenEndpointClient(
    IHttpClientFactory httpClientFactory,
    OidcClientOptions options,
    TimeProvider time)
    : ITokenEndpointClient
{
    /// <summary>Addressed at the issuer, and without the access token the API's clients attach.</summary>
    public const string HttpClientName = "ModsDudeTokenEndpoint";


    public Task<TokenSet> ExchangeCodeAsync(string code, string codeVerifier, Uri redirectUri, CancellationToken cancellationToken)
    {
        return RequestAsync(
            [
                new("grant_type", "authorization_code"),
                new("code", code),
                new("code_verifier", codeVerifier),
                new("redirect_uri", redirectUri.ToString())
            ],
            cancellationToken);
    }

    public Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        return RequestAsync(
            [
                new("grant_type", "refresh_token"),
                new("refresh_token", refreshToken)
            ],
            cancellationToken);
    }


    private async Task<TokenSet> RequestAsync(List<KeyValuePair<string, string>> parameters, CancellationToken cancellationToken)
    {
        parameters.Add(new("client_id", options.ClientId));

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await httpClientFactory.CreateClient(HttpClientName).PostAsync(OidcClientOptions.TokenPath, content, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var success = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new FormatException("The token endpoint answered with nothing.");

            return success.ToTokenSet(time.GetUtcNow());
        }

        if (await TryReadErrorAsync(response, cancellationToken) is { Error: "invalid_grant" } error)
        {
            throw new TokenGrantRejectedException(error.Error, error.Description);
        }

        throw new HttpRequestException($"The token endpoint answered {(int)response.StatusCode}.", null, response.StatusCode);
    }

    private static async Task<ErrorResponse?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }


    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("id_token")] string? IdentityToken)
    {
        public TokenSet ToTokenSet(DateTimeOffset now)
        {
            if (RefreshToken is null || IdentityToken is null)
            {
                throw new FormatException("The token endpoint left out the refresh token or the identity token.");
            }

            return new TokenSet(AccessToken, now.AddSeconds(ExpiresIn), RefreshToken, IdentityTokenReader.Read(IdentityToken));
        }
    }

    private sealed record ErrorResponse(
        [property: JsonPropertyName("error")] string Error,
        [property: JsonPropertyName("error_description")] string? Description);
}
