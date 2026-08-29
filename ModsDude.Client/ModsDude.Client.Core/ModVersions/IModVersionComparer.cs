namespace ModsDude.Client.Core.ModVersions;

/// <summary>
/// Orders two version strings belonging to the same mod.
/// </summary>
/// <remarks>
/// <para>
/// An implementation is expected to <b>abstain rather than guess</b>. A version string is whatever
/// the mod author typed, so a comparer that insists on producing an answer mis-orders releases
/// silently, and nobody finds out until a profile pins the wrong build. Where the strings do not
/// settle the order, the user arbitrates once and the answer is stored repo-wide - a question is
/// cheap, a wrong order is not.
/// </para>
/// <para>
/// Implementations must be symmetric: if <c>Compare(a, b)</c> is
/// <see cref="ModVersionComparison.Earlier"/> then <c>Compare(b, a)</c> is
/// <see cref="ModVersionComparison.Later"/>. Ordering a set does not depend on transitivity - a
/// comparer that contradicts itself degrades to more questions rather than to a wrong order - but
/// it does depend on symmetry.
/// </para>
/// </remarks>
public interface IModVersionComparer
{
    ModVersionComparison Compare(string left, string right);
}
