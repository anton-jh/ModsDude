namespace ModsDude.Server.Api.Admin;

/// <summary>
/// The one login shared by the admin page and the Hangfire dashboard, and the port they answer on.
/// Nobody gets in while any of them is unset.
/// </summary>
public class AdminOptions
{
    public const string SectionName = "Admin";


    public string Username { get; set; } = string.Empty;

    /// <summary>Kept out of the committed settings; set it through user secrets or the environment.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>A port of the API's own that only the server itself can reach.</summary>
    public int Port { get; set; }


    public bool IsConfigured => Username.Length > 0 && Password.Length > 0 && Port > 0;
}
