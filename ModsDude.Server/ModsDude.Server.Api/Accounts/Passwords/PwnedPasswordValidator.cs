using Microsoft.AspNetCore.Identity;
using ModsDude.Server.Persistence.Accounts;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Server.Api.Accounts.Passwords;

/// <summary>
/// Refuses a password that appears in Have I Been Pwned's list of breached passwords. Only the first
/// five characters of the password's SHA-1 hash leave the server.
/// </summary>
/// <remarks>
/// A password is accepted when the service cannot be reached: an outage somewhere else must not stop
/// anyone signing up or changing their password.
/// </remarks>
public class PwnedPasswordValidator(
    IHttpClientFactory httpClientFactory,
    ILogger<PwnedPasswordValidator> logger)
    : IPasswordValidator<UserAccount>
{
    public const string HttpClientName = "PwnedPasswords";
    public const string BaseAddress = "https://api.pwnedpasswords.com/";

    private const int _prefixLength = 5;


    public async Task<IdentityResult> ValidateAsync(UserManager<UserAccount> manager, UserAccount user, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return IdentityResult.Success;
        }

        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var prefix = hash[.._prefixLength];
        var suffix = hash[_prefixLength..];

        string range;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"range/{prefix}");
            request.Headers.Add("Add-Padding", "true");

            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request);
            response.EnsureSuccessStatusCode();

            range = await response.Content.ReadAsStringAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Could not check a password against Pwned Passwords, so it was accepted unchecked.");

            return IdentityResult.Success;
        }

        return IsListed(range, suffix)
            ? IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordBreached",
                Description = "This password has appeared in a data breach. Choose another."
            })
            : IdentityResult.Success;
    }


    /// <summary>
    /// Each line is a hash suffix and how often it was seen. Padding lines are seen zero times.
    /// </summary>
    public static bool IsListed(string range, string suffix)
    {
        foreach (var line in range.AsSpan().EnumerateLines())
        {
            var separator = line.IndexOf(':');

            if (separator > 0
                && line[..separator].Trim().Equals(suffix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line[(separator + 1)..].Trim(), out var count)
                && count > 0)
            {
                return true;
            }
        }

        return false;
    }
}
