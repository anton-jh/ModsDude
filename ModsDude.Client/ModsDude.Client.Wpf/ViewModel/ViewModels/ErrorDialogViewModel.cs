using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Wpf.Diagnostics;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// The app's one error dialog. Everything that went wrong and is worth interrupting somebody for
/// wears this: the same title, the same single way out, and the same two things underneath -
/// when it was logged, and the way to the log.
/// </summary>
/// <remarks>
/// <para>
/// <b>Separate from <see cref="ConfirmationDialogViewModel"/> because that one is generic.</b> It
/// still asks questions, reports refusals and lists validation errors, none of which are faults and
/// none of which have a log line to point at. Offering "Open log folder" on a "Really delete this?"
/// would be offering a dead end.
/// </para>
/// <para>
/// <b>The timestamp is what makes the log usable.</b> A folder of day-files is only worth opening if
/// you know which second to look at, and by the time somebody reads a dialog, decides to report it
/// and finds the folder, "just now" has stopped being an answer. It is stamped when the failure is
/// <em>logged</em>, not when the dialog is shown, so it matches the line it names.
/// </para>
/// <para>
/// <b>One button.</b> There is nothing here to decide - the old error dialog inherited two from the
/// confirmation it borrowed, which implied a choice that was never on offer.
/// </para>
/// </remarks>
public partial class ErrorDialogViewModel(
    string message,
    string? details,
    DateTimeOffset loggedAt,
    ILogger? logger = null)
    : ModalViewModel
{
    /// <summary>
    /// Second precision, and local. It exists to be matched against a log line's own prefix, which
    /// is written the same way.
    /// </summary>
    public const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";


    public string Title { get; } = "Oops!";

    /// <summary>What went wrong, in the terms of whoever is reading it.</summary>
    public string Message { get; } = message;

    /// <summary>
    /// The developer's half - an exception message, usually. Rendered as a secondary block rather
    /// than folded into <see cref="Message"/>, so that the sentence a user can act on is not
    /// buried under one they cannot.
    /// </summary>
    public string? Details { get; } = details;

    public bool HasDetails => string.IsNullOrWhiteSpace(Details) is false;

    public DateTimeOffset LoggedAt { get; } = loggedAt;

    [ObservableProperty]
    private string _footer = $"Logged {loggedAt.ToString(TimestampFormat)}";


    [RelayCommand]
    private void OpenLog()
    {
        if (LogFolder.TryOpen(logger) is false)
        {
            // The one thing this dialog can do besides be dismissed, and it failed. Naming the
            // folder is still better than nothing.
            Footer = $"Logged {LoggedAt.ToString(TimestampFormat)} - the log is in {LogFolder.Path}";
        }
    }

    [RelayCommand]
    private void Dismiss() => Done = true;
}
