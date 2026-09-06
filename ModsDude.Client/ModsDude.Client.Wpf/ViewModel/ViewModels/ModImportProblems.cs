using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Text;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// What an import that did not finish everything says to the user: one dialog for the whole run,
/// with the mods that did not make it grouped by reason.
/// </summary>
/// <remarks>
/// <para>
/// <b>One dialog per import, not per mod.</b> A run is one action the user took, and a batch of two
/// thousand mods that hits the same wall four hundred times is one thing that went wrong - four
/// hundred dialogs, or four hundred distinct sentences down a list, is the same information made
/// unreadable.
/// </para>
/// <para>
/// <b>Nothing here is an exception message.</b> Those are written for whoever has to fix the client,
/// and they go to the log with their stack traces attached. What a user needs is which mods are
/// missing, why in a sentence they can act on, and what that leaves them with. A mod the repo
/// already held never reaches this at all: the bytes the import wanted are in the repo, which is
/// what was asked for.
/// </para>
/// </remarks>
public static class ModImportProblems
{
    /// <summary>How many mods a reason names before the rest become a count.</summary>
    private const int _namesShown = 6;


    /// <summary>
    /// The dialog for a finished import, or null when there is nothing to report - which is the
    /// ordinary case and must not raise anything.
    /// </summary>
    /// <param name="nameOf">
    /// How a mod version is named to the user. The identity is a pair of ids; the list it came from
    /// is the only thing that knows what the row was called.
    /// </param>
    /// <param name="consequence">
    /// What the failures cost, in the caller's terms - a save that wrote nothing, say. A few words,
    /// not a paragraph: this is the top of a dialog, and the reasons under it are the point of it.
    /// </param>
    public static ErrorDialogViewModel? Build(
        IErrorReporter errorReporter,
        ModImportResult result,
        Func<ModVersionIdentity, string> nameOf,
        string? consequence = null)
    {
        var problems = result.Unfinished;

        if (problems.Count == 0)
        {
            return null;
        }

        var message = new StringBuilder(problems.Count == 1
            ? "One mod did not make it in."
            : $"{problems.Count} mods did not make it in.");

        if (consequence is not null)
        {
            message.Append(' ').Append(consequence);
        }

        // Failures first: they are the only group here that is a fault rather than a decision, and
        // the only one the reader may not have been expecting.
        var groups = problems
            .GroupBy(x => x.Status)
            .OrderBy(x => x.Key is ModImportStatus.Failed ? 0 : 1)
            .ThenBy(x => x.Key);

        foreach (var group in groups)
        {
            message
                .Append("\n\n")
                .Append(DescribeGroup(group.Key))
                .Append(' ')
                .Append(NameThem(group, nameOf));
        }

        // Through the reporter like every other error: the per-mod exceptions are already in the
        // log, and this is the line that says which run they belonged to.
        return errorReporter.Record(
            message.ToString(),
            context: "importing mods");
    }

    /// <summary>
    /// The mark's tooltip: which of these the row was, for whoever comes back to the list after the
    /// dialog is gone.
    /// </summary>
    public static string DescribeRow(ModImportStatus? status) => status switch
    {
        ModImportStatus.SourceConflict => "Not imported: two enabled sources hold different files for this one.",
        ModImportStatus.ContentMismatch => "Not imported: the repo already stores a different file under this version.",
        ModImportStatus.NeedsArbitration => "Not imported: this mod's version order was never settled.",
        ModImportStatus.NoLocalFile => "Not imported: there is no local file to upload.",
        ModImportStatus.Failed => "This mod could not be imported.",
        _ => "This mod was not imported."
    };

    /// <summary>
    /// The line over one reason's mods. One clause for what happened and, where there is something
    /// to do about it, one for that - three of these are a choice the user has to make, and only the
    /// last is a fault.
    /// </summary>
    private static string DescribeGroup(ModImportStatus status) => status switch
    {
        ModImportStatus.SourceConflict => "Two sources disagree about these; disable one of them:",
        ModImportStatus.ContentMismatch => "The repo already stores a different file for these:",
        ModImportStatus.NeedsArbitration => "Their version order was never settled:",
        ModImportStatus.NoLocalFile => "No local file to upload for these:",
        _ => "Uploading or registering went wrong; worth another go:"
    };

    private static string NameThem(IEnumerable<ModImportItemResult> items, Func<ModVersionIdentity, string> nameOf)
    {
        var names = items.Select(x => nameOf(x.Identity)).ToList();

        var shown = string.Join(", ", names.Take(_namesShown));

        return names.Count > _namesShown
            ? $"{shown} and {names.Count - _namesShown} more"
            : shown;
    }
}
