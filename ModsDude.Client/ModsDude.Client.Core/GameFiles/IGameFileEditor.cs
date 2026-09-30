using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.GameFiles;

/// <summary>Carries out the <see cref="GameFileEdit"/>s adapters ask for. Adapters never write themselves.</summary>
public interface IGameFileEditor
{
    /// <summary>What applying the edit now would do, without writing anything.</summary>
    PlannedFileEdit Plan(string folder, GameFileEdit edit);

    /// <summary>
    /// Applies the edit to the file as it is now, atomically. Running it again changes nothing.
    /// </summary>
    /// <returns>Whether the file changed.</returns>
    bool Apply(string folder, GameFileEdit edit);
}

public sealed record PlannedFileEdit(GameFileEdit Edit, string FullPath, bool Changes);
