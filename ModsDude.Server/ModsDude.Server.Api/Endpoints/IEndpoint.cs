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
        var types = assembly
            .GetTypes()
            .Except([typeof(IEndpoint)])
            .Where(x => x.IsAssignableTo(typeof(IEndpoint)));

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
