namespace ModsDude.Server.Api.Builds;

public class ClientDownloadOptions
{
    public const string SectionName = "ClientDownload";


    /// <summary>The public repo whose releases hold the client installers.</summary>
    public string GithubRepository { get; set; } = "";
}
