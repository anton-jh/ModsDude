using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Authentication;
using ModsDude.Client.Core.Helpers;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// The refresh token in a file in the app's data folder, encrypted with DPAPI so only this Windows user
/// can read it.
/// </summary>
public sealed class ProtectedTokenStore(ILogger<ProtectedTokenStore> logger) : ITokenStore
{
    private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("ModsDude refresh token");

    private readonly string _path = Path.Combine(FileSystemHelper.GetAppDataDirectory(), "session.dat");


    public async Task<string?> LoadRefreshTokenAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken);

        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, _entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException exception)
        {
            // Written by another Windows user, or damaged. Either way there is nothing to sign in with.
            logger.LogWarning(exception, "Could not read the kept refresh token, so somebody has to sign in.");

            return null;
        }
    }

    public Task SaveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(refreshToken), _entropy, DataProtectionScope.CurrentUser);

        return AtomicFile.WriteAsync(_path, stream => stream.WriteAsync(protectedBytes, cancellationToken).AsTask());
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        File.Delete(_path);

        return Task.CompletedTask;
    }
}
