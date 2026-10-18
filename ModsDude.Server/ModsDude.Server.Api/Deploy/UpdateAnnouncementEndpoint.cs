using Microsoft.AspNetCore.Http.HttpResults;

namespace ModsDude.Server.Api.Deploy;

/// <summary>
/// <c>PUT</c> and <c>DELETE /deploy/update-announcement</c>: CI announcing that it has started shipping
/// a build, and withdrawing that when the run ends without replacing this server. Outside the API and
/// its document, because CI is not a client.
/// </summary>
public static class UpdateAnnouncementEndpoint
{
    public static IEndpointRouteBuilder MapUpdateAnnouncement(this IEndpointRouteBuilder builder)
    {
        builder.MapPut("deploy/update-announcement", Announce)
            .RequireAuthorization(DeployAuthenticationHandler.PolicyName)
            .ExcludeFromDescription();

        builder.MapDelete("deploy/update-announcement", Withdraw)
            .RequireAuthorization(DeployAuthenticationHandler.PolicyName)
            .ExcludeFromDescription();

        return builder;
    }


    private static NoContent Announce(IUpdateAnnouncement announcement)
    {
        announcement.Announce();

        return TypedResults.NoContent();
    }

    private static NoContent Withdraw(IUpdateAnnouncement announcement)
    {
        announcement.Withdraw();

        return TypedResults.NoContent();
    }
}
