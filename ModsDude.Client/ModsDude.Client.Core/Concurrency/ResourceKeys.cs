using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Concurrency;

/// <summary>
/// The name of every resource a lease can be taken on, spelled in exactly one place.
/// </summary>
/// <remarks>
/// <para>
/// A lease is only worth anything if two call sites claiming the same thing produce the same string,
/// and the three resources make that easy to get wrong in three different ways: a mod folder is
/// identified by a struct, a repo by a <see cref="Guid"/> whose <c>ToString</c> has a casing, and a
/// store by a path - which is the one that matters, because <c>D:\store</c> and <c>d:\Store\</c> are
/// the same store and would otherwise be two leases that never see each other.
/// </para>
/// <para>
/// The prefixes are there so a holder list read off <see cref="IResourceLeases.DescribeAll"/> in a
/// log says what kind of thing was held, and so a repo id can never collide with a target key.
/// </para>
/// </remarks>
public static class ResourceKeys
{
    /// <summary>
    /// One mod folder - the unit an apply actually works on, and the one that ends up holding a
    /// mixture of two profiles if two applies run at once.
    /// </summary>
    /// <remarks>
    /// <see cref="ModTargetRef"/> rather than the path, because the path is a consequence of settings
    /// that can change under a running sync, and because two games pointed at one folder are still two
    /// targets with two manifests - which is the thing being protected.
    /// </remarks>
    public static string Target(ModTargetRef target) => $"target:{target}";

    /// <summary>One repo's registered mod set, which is what an import writes into.</summary>
    public static string Repo(Guid repoId) => $"repo:{repoId:N}";

    /// <summary>
    /// One profile's mod list, which is what a save writes a revision of.
    /// </summary>
    /// <remarks>
    /// <b>The claim is on the profile, not on the page that started the save.</b> A save writes a
    /// revision of one profile and imports into one repo, and both of those outlive whatever started
    /// them - so an editor rebuilt for a profile that is already being saved has to be able to find
    /// that out, and a second save of the same list has to be refused rather than raced. The repo
    /// lease the import takes sits underneath this one; they are different resources, which is why a
    /// draft with nothing to import can still be saved beside an import into the same repo.
    /// </remarks>
    public static string Profile(Guid profileId) => $"profile:{profileId:N}";

    /// <summary>
    /// One content store, by the folder it lives in.
    /// </summary>
    /// <remarks>
    /// Normalised, because a store is reached through <see cref="Sync.IContentStoreProvider"/>, which
    /// builds a fresh handle every call from a path that came out of settings on one route and off a
    /// mod folder's volume on another. Those two spellings must be one lease.
    /// </remarks>
    public static string Store(string rootPath) => $"store:{FileSystemHelper.NormalizePathForComparison(rootPath)}";

    /// <inheritdoc cref="Store(string)"/>
    public static string Store(Sync.ContentStore store) => Store(store.RootPath);
}
