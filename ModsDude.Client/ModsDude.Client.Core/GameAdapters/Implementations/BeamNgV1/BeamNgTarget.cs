namespace ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;

/// <summary>
/// The folders BeamNG.drive and BeamMP reach, by key.
/// </summary>
/// <remarks>
/// <b>Stable, and they have to stay that way.</b> Manifests are filed under these, so renaming one
/// orphans every machine's record of that folder.
/// </remarks>
public static class BeamNgTarget
{
    /// <summary>The game's own <c>mods</c> folder.</summary>
    public static TargetKey Game { get; } = new("game");

    /// <summary>The BeamMP server's client mods folder, <c>Resources/Client</c> on a stock server.</summary>
    public static TargetKey Server { get; } = new("server");

    /// <summary>The BeamMP launcher's download cache, which joining a server reads before it downloads.</summary>
    public static TargetKey Client { get; } = new("client");
}
