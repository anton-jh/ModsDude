namespace ModsDude.Client.Wpf.Account;

/// <summary>The Entra External ID tenant and app registration the client signs in with.</summary>
public sealed record AuthenticationOptions
{
    public const string SectionName = "Authentication";


    /// <summary>The client's own app registration: a public client with <c>http://localhost</c> as a redirect URI.</summary>
    public string ClientId { get; init; } = "";

    /// <summary><c>https://{tenant}.ciamlogin.com/{tenant ID}</c></summary>
    public string Authority { get; init; } = "";

    /// <summary>The scope the API's app registration exposes.</summary>
    public string Scope { get; init; } = "";


    public bool IsComplete
        => ClientId.Length > 0 && Authority.Length > 0 && Scope.Length > 0;
}
