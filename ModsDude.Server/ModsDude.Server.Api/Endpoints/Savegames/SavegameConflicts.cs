using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Persistence.EntityTypeConfigurations;
using Npgsql;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// Which rule a savegame write actually broke, for the two endpoints that can break more than one.
/// </summary>
/// <remarks>
/// <para>
/// A unique violation says which index it broke and nothing else, so an endpoint that inserts a
/// savegame has to read the name back to tell a clash over "Season 4" from somebody else publishing
/// to the same profile in the same instant. Both are ordinary races; only the sentences differ, and
/// a person told about the wrong one goes looking for a problem that is not there.
/// </para>
/// <para>
/// The name comes from <see cref="SavegameIndexNames"/> rather than a literal here, so the model and
/// the refusal cannot come to disagree about what the index is called.
/// </para>
/// </remarks>
internal static class SavegameConflicts
{
    /// <summary>
    /// Whether the write lost the profile's one current-savegame slot to somebody else's.
    /// </summary>
    public static bool IsCurrentSavegameConflict(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            && postgres.ConstraintName == SavegameIndexNames.OneCurrentSavegamePerProfile;
    }
}
