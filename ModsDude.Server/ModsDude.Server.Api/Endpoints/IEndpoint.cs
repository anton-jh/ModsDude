using System.Reflection;

namespace ModsDude.Server.Api.Endpoints;

public interface IEndpoint
{
    RouteHandlerBuilder Map(IEndpointRouteBuilder builder);
}


public static class EndpointMappingExtensions
{
    public static IEndpointRouteBuilder MapAllEndpointsFromAssembly(this IEndpointRouteBuilder builder, Assembly assembly)
    {
        // Ordered by name because the OpenAPI document is checked in and diffed in CI: registration
        // order decides the order of paths and schemas in it, and Assembly.GetTypes() promises no
        // order at all, so an undecided one would show up as spurious drift.
        var types = assembly
            .GetTypes()
            .Except([typeof(IEndpoint)])
            .Where(x => x.IsAssignableTo(typeof(IEndpoint)))
            .OrderBy(x => x.FullName, StringComparer.Ordinal);

        foreach (var type in types)
        {
            var instance = (IEndpoint)Activator.CreateInstance(type)!;
            var routeHandlerBuilder = instance.Map(builder);

            var name = type.Name[..type.Name.IndexOf("Endpoint")];
            routeHandlerBuilder.WithName(name);
        }

        return builder;
    }
}
