namespace ModsDude.Client.Core.Authentication;

public interface IBrowserLauncher
{
    /// <summary>Opens the address in the user's default browser.</summary>
    void Open(Uri address);
}
