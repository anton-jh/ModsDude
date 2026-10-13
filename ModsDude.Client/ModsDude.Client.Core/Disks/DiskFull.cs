namespace ModsDude.Client.Core.Disks;

public static class DiskFull
{
    private const int _errorDiskFull = unchecked((int)0x80070070);
    private const int _errorHandleDiskFull = unchecked((int)0x80070027);


    /// <summary>Whether <paramref name="exception"/>, or anything it wraps, is a disk running out of space.</summary>
    public static bool Is(Exception exception)
        => exception is IOException { HResult: _errorDiskFull or _errorHandleDiskFull }
            || (exception.InnerException is Exception inner && Is(inner));
}
