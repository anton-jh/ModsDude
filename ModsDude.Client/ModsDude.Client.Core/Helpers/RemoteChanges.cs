namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// What a background check found on the server that a list on this machine does not show yet, one
/// sentence per difference.
/// </summary>
/// <remarks>
/// <b>Found, not applied.</b> The list is left as it was until somebody asks for the refresh: a
/// sidebar that rearranges itself under the pointer is worse than one that is a few minutes behind
/// and says so. This is what "says so" reads from.
/// </remarks>
public sealed class RemoteChanges(IReadOnlyList<string> lines)
{
    /// <summary>
    /// Beyond this the tooltip stops being something anybody reads, and what it says is only "a lot" -
    /// which a count says better.
    /// </summary>
    private const int _maxShown = 5;


    public IReadOnlyList<string> Lines { get; } = lines;


    /// <summary>
    /// The lines as the refresh button's tooltip shows them, shortened to a count past a handful.
    /// </summary>
    public string Describe()
    {
        if (Lines.Count <= _maxShown)
        {
            return string.Join(Environment.NewLine, Lines);
        }

        // One fewer than the maximum, so that the "and more" line does not push it past it.
        var shown = Lines.Take(_maxShown - 1).Append($"And {Lines.Count - (_maxShown - 1)} more.");

        return string.Join(Environment.NewLine, shown);
    }
}
