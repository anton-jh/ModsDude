using Microsoft.Win32;
using System.Runtime.Versioning;

namespace ModsDude.Client.Core.Startup;

/// <summary><see cref="IStartupRegistry"/> over the current user's real registry.</summary>
/// <remarks>
/// Per user, never per machine: nothing here needs elevation, and an entry under
/// <c>HKLM</c> would start the app for people who never chose it.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class RegistryStartupRegistry : IStartupRegistry
{
    private const string _runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string _approvalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";


    public string? GetRunCommand(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKey);

        return key?.GetValue(name) as string;
    }

    public void SetRunCommand(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_runKey);

        key.SetValue(name, command, RegistryValueKind.String);
    }

    public void RemoveRunCommand(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKey, writable: true);

        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public byte[]? GetApproval(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_approvalKey);

        return key?.GetValue(name) as byte[];
    }

    public void RemoveApproval(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_approvalKey, writable: true);

        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
