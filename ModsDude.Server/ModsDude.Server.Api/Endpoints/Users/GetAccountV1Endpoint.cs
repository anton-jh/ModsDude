using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Application.Exceptions;
using ModsDude.Server.Persistence.DbContexts;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Users;

/// <summary>How the caller signs in. Changed on the account pages, not through the API.</summary>
public class GetAccountV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("users/me/account", GetAccount)
            .WithTags("Users");
    }


    private async Task<Ok<AccountDto>> GetAccount(
        ApplicationDbContext dbContext,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        var accountId = claimsPrincipal.GetUserId().Value;

        var account = await dbContext.UserAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == accountId, cancellationToken)
            ?? throw new NotAuthenticatedException();

        return TypedResults.Ok(new AccountDto(account.Email ?? ""));
    }
}
