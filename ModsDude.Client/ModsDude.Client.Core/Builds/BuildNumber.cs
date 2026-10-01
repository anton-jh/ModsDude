using System.Globalization;
using System.Reflection;

namespace ModsDude.Client.Core.Builds;

/// <summary>
/// A build of ModsDude: the count of commits on main it was built from, shown as <c>bN</c>. The client
/// and the server of one build are built together and only ever talk to each other. 0 is a local build.
/// </summary>
public readonly record struct BuildNumber(int Value)
{
    /// <summary>This copy's, stamped by Directory.Build.props.</summary>
    public static BuildNumber Current { get; } = new(int.Parse(
        typeof(BuildNumber).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(x => x.Key == "BuildNumber")
            .Value!,
        CultureInfo.InvariantCulture));


    public bool IsLocal => Value == 0;


    public override string ToString() => IsLocal ? "dev" : $"b{Value}";
}
