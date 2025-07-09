namespace ModsDude.Shared.Endpoints;

public interface IEndpointDefinition<TResponse>
{
    string PathTemplate { get; }
    Task<TResponse> Call();
}
