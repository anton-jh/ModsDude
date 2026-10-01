using System.Globalization;
using System.Reflection;

namespace ModsDude.Server.Api.Builds;

/// <summary>The build number this server was stamped with; see Directory.Build.props. 0 is a local build.</summary>
public static class ServerBuild
{
    public static int Number { get; } = int.Parse(
        typeof(ServerBuild).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(x => x.Key == "BuildNumber")
            .Value!,
        CultureInfo.InvariantCulture);
}
