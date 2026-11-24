namespace ModsDude.Server.Persistence.Storage;

public interface IStorageGatekeeper
{
    Task<StorageInspection> InspectAsync(int minimumSoftDeleteDays, CancellationToken cancellationToken);

    /// <summary>Creates the binding, marks empty containers and upgrades old markers, as the inspection decided.</summary>
    /// <returns>Why storage may not be written to after all: a marker changed under the run.</returns>
    Task<IReadOnlyList<string>> PrepareAsync(StorageInspection inspection, CancellationToken cancellationToken);

    /// <summary>Moves the generation on, in the database and in every marker.</summary>
    /// <exception cref="InvalidOperationException">A marker changed during the run.</exception>
    Task AdvanceAsync(CancellationToken cancellationToken);

    /// <summary>Takes storage over for this database, whatever the markers say.</summary>
    /// <returns>What went wrong, or nothing where every marker was written.</returns>
    Task<IReadOnlyList<string>> BindByHandAsync(string operatorName, CancellationToken cancellationToken);
}
