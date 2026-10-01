using System.Reflection;

namespace ModsDude.Client.Core.Builds;

/// <summary>What this copy of the app is, in the words shown to the user.</summary>
public static class AppBuild
{
    /// <summary><c>b42</c>, or <c>dev (abc1234)</c> for a local build, which has no number of its own.</summary>
    public static string Description { get; } = Describe(BuildNumber.Current, ReadCommit());


    public static string Describe(BuildNumber build, string? commit)
    {
        return build.IsLocal && commit is { Length: > 0 }
            ? $"{build} ({commit[..Math.Min(7, commit.Length)]})"
            : build.ToString();
    }


    /// <summary>The SDK appends the commit to the informational version: <c>0.0.42+&lt;sha&gt;</c>.</summary>
    private static string? ReadCommit()
    {
        var version = typeof(AppBuild).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return version?.IndexOf('+') is int plus and >= 0
            ? version[(plus + 1)..]
            : null;
    }
}
