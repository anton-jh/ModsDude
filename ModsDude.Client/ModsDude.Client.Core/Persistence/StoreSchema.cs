using System.Text.Json.Nodes;

namespace ModsDude.Client.Core.Persistence;

/// <summary>
/// The versions of a persisted file this build can read, and how to carry each one forward.
/// </summary>
/// <param name="Migrations">
/// One per version step, oldest first: the first turns <paramref name="OldestVersion"/> into the one
/// after it. Each takes the whole file and returns it in the next version's shape.
/// </param>
public sealed record StoreSchema(int CurrentVersion, int OldestVersion, IReadOnlyList<Func<JsonObject, JsonObject>> Migrations)
{
    /// <summary>The property every persisted file records its version in.</summary>
    public const string VersionProperty = "Version";

    public StoreSchema Validate()
    {
        if (OldestVersion > CurrentVersion || Migrations.Count != CurrentVersion - OldestVersion)
        {
            throw new InvalidOperationException(
                $"A schema reading versions {OldestVersion} to {CurrentVersion} needs {CurrentVersion - OldestVersion} migrations, not {Migrations.Count}.");
        }

        return this;
    }
}
