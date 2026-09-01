using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Helpers;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace ModsDude.Client.Wpf.Diagnostics;

/// <summary>
/// The client's only log sink. A WPF app has no console, so without a file behind
/// <see cref="ILogger"/> a logged failure is the same as an unlogged one - which is exactly how
/// imagery uploads managed to fail invisibly for as long as they did.
/// </summary>
/// <remarks>
/// Deliberately small: one file per day under %LocalAppData%\ModsDude\logs, appended under a lock,
/// older files dropped at startup. A desktop client writing a few hundred lines a session does not
/// need a logging framework, and a dependency that must be configured before it works is a
/// dependency that ships misconfigured.
/// </remarks>
public sealed class FileLoggerProvider : ILoggerProvider
{
    /// <summary>Long enough to cover "it was broken last week", short enough to bound the folder.</summary>
    private static readonly TimeSpan _retention = TimeSpan.FromDays(14);

    /// <summary>Where the files are, for anything that wants to point a user at them.</summary>
    public static string LogDirectory { get; } = Path.Combine(FileSystemHelper.GetAppDataDirectory(), "logs");

    private readonly ConcurrentDictionary<string, FileLogger> _loggers = [];
    private readonly Lock _writeLock = new();
    private readonly string _directory;


    public FileLoggerProvider()
    {
        _directory = LogDirectory;

        TryPrepareDirectory();
    }


    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }


    private void Write(string line)
    {
        // Logging must never be the thing that takes the app down: a locked file or a full disk
        // costs the line, not the session.
        try
        {
            lock (_writeLock)
            {
                File.AppendAllText(GetCurrentFilePath(), line, Encoding.UTF8);
            }
        }
        catch (Exception)
        {
        }
    }

    private string GetCurrentFilePath()
    {
        return Path.Combine(_directory, $"client-{DateTime.UtcNow:yyyyMMdd}.log");
    }

    private void TryPrepareDirectory()
    {
        try
        {
            System.IO.Directory.CreateDirectory(_directory);

            var cutoff = DateTime.UtcNow - _retention;

            foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "client-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception)
        {
        }
    }


    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel) is false)
            {
                return;
            }

            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append(" [").Append(Abbreviate(logLevel)).Append("] ")
                .Append(category)
                .Append(": ")
                .AppendLine(formatter(state, exception));

            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            provider.Write(builder.ToString());
        }


        private static string Abbreviate(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "___"
        };
    }
}
