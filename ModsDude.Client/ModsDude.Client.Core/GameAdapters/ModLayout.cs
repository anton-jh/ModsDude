using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.GameAdapters;

/// <summary>Everything a mod folder is about to hold, for the adapter to lay out as one.</summary>
/// <param name="Desired">Every mod version the folder should end up with. Empty clears it.</param>
public sealed record ModLayoutContext(ModTarget Target, IReadOnlyList<ModLayoutMod> Desired);

/// <param name="ContentHash">The SHA-256 of the file, as lowercase hex.</param>
/// <param name="FileName">
/// What the repo registered the file as, or null where it has nothing usable. Already checked to be a
/// bare name belonging to <paramref name="ModId"/>.
/// </param>
/// <param name="InstalledFileName">The file in the mod folder holding this mod now, whatever its version, if any.</param>
public sealed record ModLayoutMod(
    ModKey ModId,
    ModVersionKey VersionId,
    string ContentHash,
    ModFileName? FileName,
    string? InstalledFileName,
    bool Locked);

/// <param name="Placements">One per desired mod: the file name it gets directly in the mod folder.</param>
/// <param name="ManagedFiles">Other files the game needs changed to match, relative to the mod folder.</param>
public sealed record ModLayout(IReadOnlyList<ModPlacement> Placements, IReadOnlyList<GameFileEdit> ManagedFiles)
{
    public string FileNameOf(ModKey modId)
        => Placements.FirstOrDefault(x => x.ModId == modId)?.FileName
            ?? throw new InvalidOperationException($"The layout placed no file for '{modId}'.");
}

public sealed record ModPlacement(ModKey ModId, string FileName);
