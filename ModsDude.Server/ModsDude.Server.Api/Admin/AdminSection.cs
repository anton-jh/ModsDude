namespace ModsDude.Server.Api.Admin;

/// <summary>Which admin page is showing, so the layout can mark it in the navigation.</summary>
public enum AdminSection
{
    Overview,
    Repos,
    Users,
    TrustCodes
}


public static class AdminLayout
{
    /// <summary>The <c>ViewData</c> entry a page puts its <see cref="AdminSection"/> in.</summary>
    public const string SectionKey = "AdminSection";
}
