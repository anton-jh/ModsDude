using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Accounts;
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
            "Shown to the user at {LoggedAt} while {Context}: {UserMessage}{DeveloperMessage}",
            loggedAt.ToString(ErrorModalViewModel.TimestampFormat),
            context ?? "working",
            friendly.UserMessage,
            friendly.DeveloperMessage == friendly.UserMessage ? "" : $" ({friendly.DeveloperMessage})");

        return new ErrorModalViewModel(friendly.UserMessage, friendly.Hint, loggedAt, logger);
    }

    public ErrorModalViewModel Record(string message, string? hint = null, string? context = null)
    {
        var loggedAt = DateTimeOffset.Now;

        logger.LogError(
            "Shown to the user at {LoggedAt} while {Context}: {UserMessage} {Hint}",
            loggedAt.ToString(ErrorModalViewModel.TimestampFormat),
            context ?? "working",
            message,
            hint ?? "");

        return new ErrorModalViewModel(message, hint, loggedAt, logger);
    }

    public Task ShowAsync(Exception exception, string? context = null)
    {
        // The window is covered by the build mismatch or the account block, which is the one place
        // that says so.
        if (BuildRefusal.Is(exception))
        {
            logger.LogWarning(exception, "Not shown while {Context}: the server refuses this build.", context ?? "working");

            return Task.CompletedTask;
        }

        if (AccountRefusal.Is(exception))
        {
            logger.LogWarning(exception, "Not shown while {Context}: the account is blocked.", context ?? "working");

            return Task.CompletedTask;
        }

        return modalService.Value.Show(Record(exception, context));
    }
}
