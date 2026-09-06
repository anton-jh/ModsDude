using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.Diagnostics;

/// <inheritdoc cref="IErrorReporter"/>
public sealed class ErrorReporter(
    ILogger<ErrorReporter> logger,
    Lazy<IModalService> modalService)
    : IErrorReporter
{
    public ErrorDialogViewModel Record(Exception exception, string? context = null)
    {
        var friendly = exception as UserFriendlyException ?? UserFriendlyException.WrapUnknown(exception);

        var loggedAt = DateTimeOffset.Now;

        // The original exception, not the wrapper: WrapUnknown keeps the inner one but a caller that
        // threw a UserFriendlyException itself has the stack that matters on the outer.
        logger.LogError(
            exception,
            "Shown to the user at {LoggedAt} while {Context}: {UserMessage}",
            loggedAt.ToString(ErrorDialogViewModel.TimestampFormat),
            context ?? "working",
            friendly.UserMessage);

        return new ErrorDialogViewModel(friendly.UserMessage, friendly.DeveloperMessage, loggedAt, logger);
    }

    public ErrorDialogViewModel Record(string message, string? details = null, string? context = null)
    {
        var loggedAt = DateTimeOffset.Now;

        logger.LogError(
            "Shown to the user at {LoggedAt} while {Context}: {UserMessage} {Details}",
            loggedAt.ToString(ErrorDialogViewModel.TimestampFormat),
            context ?? "working",
            message,
            details ?? "");

        return new ErrorDialogViewModel(message, details, loggedAt, logger);
    }

    public Task ShowAsync(Exception exception, string? context = null)
    {
        return modalService.Value.Show(Record(exception, context));
    }
}
