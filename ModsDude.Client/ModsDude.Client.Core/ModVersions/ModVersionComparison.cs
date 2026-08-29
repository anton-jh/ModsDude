namespace ModsDude.Client.Core.ModVersions;

/// <summary>
/// The outcome of comparing two version strings of one mod.
/// </summary>
/// <remarks>
/// Deliberately not an <see cref="int"/> in the <see cref="IComparer{T}"/> convention: abstention
/// is an ordinary outcome here, and a caller that treated it as a number would quietly fold it
/// into "equal" and then hand the whole thing to <c>OrderBy</c>.
/// </remarks>
public enum ModVersionComparison
{
    /// <summary>
    /// Nothing in the two strings settles which came first. The default, so a value that was never
    /// assigned abstains rather than claiming an order.
    /// </summary>
    Undecidable = 0,

    /// <summary>The left version comes before the right one.</summary>
    Earlier,

    /// <summary>The two strings name the same release.</summary>
    Equal,

    /// <summary>The left version comes after the right one.</summary>
    Later
}
