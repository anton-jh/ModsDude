namespace ModsDude.Server.Api.Maintenance;

public class HangfireDashboardOptions
{
    public const string SectionName = "HangfireDashboard";


    public string Path { get; set; } = "/hangfire";

    /// <summary>
    /// The dashboard's one login. The dashboard is not mapped at all while either this or
    /// <see cref="Password"/> is empty, so a deployment that never set them exposes nothing.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Kept out of the committed settings; set it through user secrets or the environment.</summary>
    public string Password { get; set; } = string.Empty;


    public bool IsConfigured => Username.Length > 0 && Password.Length > 0;
}
