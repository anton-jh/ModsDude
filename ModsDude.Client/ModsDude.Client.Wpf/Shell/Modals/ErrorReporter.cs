using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Builds;
using ModsDude.Client.Core.Exceptions;

namespace ModsDude.Client.Wpf.Shell.Modals;

/// <inheritdoc cref="IErrorReporter"/>
public sealed class ErrorReporter(
    ILogger<ErrorReporter> logger,
    Lazy<IModalService> modalService)
    : IErrorReporter
{
    public ErrorModalViewModel Record(Exception exception, string? context = null)
    {
        var friendly = exception as UserFriendlyException ?? UserFriendlyException.WrapUnknown(exception);

        var loggedAt = DateTimeOffset.Now;

        // The original exception, not the wrapper: WrapUnknown keeps the inner one but a caller that
        // threw a UserFriendlyException itself has the stack that matters on the outer.
        logger.LogError(
            exception,
            "Shown to the user at {LoggedAt} while {Context}: {UserMessage}",
            loggedAt.ToString(ErrorModalViewModel.TimestampFormat),
            context ?? "working",
            friendly.UserMessage);

        return new ErrorModalViewModel(friendly.UserMessage, friendly.DeveloperMessage, loggedAt, logger);
    }

    public ErrorModalViewModel Record(string message, string? details = null, string? context = null)
    {
        var loggedAt = DateTimeOffset.Now;

        logger.LogError(
            "Shown to the user at {LoggedAt} while {Context}: {UserMessage} {Details}",
            loggedAt.ToString(ErrorModalViewModel.TimestampFormat),
            context ?? "working",
            message,
            details ?? "");

        return new ErrorModalViewModel(message, details, loggedAt, logger);
    }

    public Task ShowAsync(Exception exception, string? context = null)
    {
        // The window is covered by the build mismatch, which is the one place that says so.
        if (BuildRefusal.Is(exception))
        {
            logger.LogWarning(exception, "Not shown while {Context}: the server refuses this build.", context ?? "working");

            return Task.CompletedTask;
        }

        return modalService.Value.Show(Record(exception, context));
    }
}
