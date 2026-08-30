using Microsoft.AspNetCore.Http.Metadata;
using System.Reflection;

namespace ModsDude.Server.Api.ErrorHandling;

/// <summary>
/// A problem-details body returned at 403.
/// </summary>
/// <remarks>
/// <c>TypedResults</c> has a generic result for 400 and none for 403: <c>ForbidHttpResult</c> carries
/// an authentication scheme and no body at all. The body is the whole point of an authorization
/// refusal here — it names the membership level the operation needed — so the status needs a result
/// type of its own. Implementing <see cref="IEndpointMetadataProvider"/> is what puts the status and
/// the schema into the OpenAPI document, which is what makes the generated client parse the typed
/// problem at 403 rather than throwing an untyped <c>ApiException</c>.
/// </remarks>
public sealed class Forbidden<TValue>(TValue value)
    : IResult, IEndpointMetadataProvider, IStatusCodeHttpResult, IValueHttpResult, IValueHttpResult<TValue>
{
    public TValue Value { get; } = value;

    public int StatusCode => StatusCodes.Status403Forbidden;


    object? IValueHttpResult.Value => Value;
    int? IStatusCodeHttpResult.StatusCode => StatusCode;


    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCode;

        return httpContext.Response.WriteAsJsonAsync(Value);
    }

    static void IEndpointMetadataProvider.PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status403Forbidden,
            typeof(TValue),
            ["application/json"]));
    }
}
