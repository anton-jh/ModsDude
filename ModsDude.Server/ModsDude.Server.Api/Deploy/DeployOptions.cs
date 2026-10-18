namespace ModsDude.Server.Api.Deploy;

/// <summary>The token CI calls the deploy endpoints with. Nobody gets in while it is empty.</summary>
public class DeployOptions
{
    public const string SectionName = "Deploy";


    /// <summary>Kept out of the committed settings; set it through user secrets or the environment.</summary>
    public string Token { get; set; } = string.Empty;


    public bool IsConfigured => Token.Length > 0;
}
