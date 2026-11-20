using ModsDude.Client.Core.Authentication;
using System.Diagnostics;

namespace ModsDude.Client.Wpf.Account;

public sealed class ShellBrowserLauncher : IBrowserLauncher
{
    public void Open(Uri address)
    {
        using var _ = Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
    }
}
