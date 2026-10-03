namespace ModsDude.Server.Api.Admin;

/// <summary>
/// The one login shared by the admin page and the Hangfire dashboard. Nobody gets in while either
/// value is empty.
/// </summary>
public class AdminOptions
{
    public const string SectionName = "Admin";


    public string Username { get; set; } = string.Empty;

    /// <summary>Kept out of the committed settings; set it through user secrets or the environment.</summary>
    public string Password { get; set; } = string.Empty;


    public bool IsConfigured => Username.Length > 0 && Password.Length > 0;
}
