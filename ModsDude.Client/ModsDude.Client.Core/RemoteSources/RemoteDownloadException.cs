using ModsDude.Client.Core.Exceptions;

namespace ModsDude.Client.Core.RemoteSources;

/// <summary>A download that ended in something other than the archive, said in words for the user.</summary>
public sealed class RemoteDownloadException(string userMessage, string? developerMessage = null, Exception? inner = null)
    : UserFriendlyException(userMessage, developerMessage, inner)
{
    public static RemoteDownloadException Gone(string source) => new($"{source} no longer has this file.");

    public static RemoteDownloadException NotAnArchive(string? detail = null) => new("The download is not a mod archive.", detail);

    public static RemoteDownloadException TooLarge(long limit) => new(
        $"The download is bigger than {Helpers.ByteSize.Describe(limit)}.");

    public static RemoteDownloadException NotAllowed(Uri uri) => new(
        "The download points somewhere that is not allowed.",
        $"Refused to download from {uri.Scheme}://{uri.Host}.");

    public static RemoteDownloadException TooManyRedirects() => new("The download kept redirecting.");

    public static RemoteDownloadException BadFileName(string fileName) => new(
        "The download has a file name that cannot be used.",
        $"Refused the file name '{fileName}'.");
}
