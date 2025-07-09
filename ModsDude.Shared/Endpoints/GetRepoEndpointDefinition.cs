
namespace ModsDude.Shared.Endpoints;
public abstract class GetRepoEndpointDefinition : IEndpointDefinition<GetRepoResponse>
{
    public string PathTemplate { get; } = "/repos/{repoId}";

    public Task<GetRepoResponse> Call()
    {
        throw new NotImplementedException();
    }
}

public record GetRepoResponse();
