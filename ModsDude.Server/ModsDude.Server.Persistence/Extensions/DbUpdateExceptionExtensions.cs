using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ModsDude.Server.Persistence.Extensions;

/// <summary>
/// Which constraint refused a write. The database backs every invariant it can, so a concurrent request
/// that slipped past an endpoint's own check lands here, and the endpoint answers it as the outcome it
/// expected rather than as a failure.
/// </summary>
public static class DbUpdateExceptionExtensions
{
    /// <param name="constraint">The index or key that must have been violated, or null for any.</param>
    public static bool IsUniqueViolation(this DbUpdateException exception, string? constraint = null)
        => Violates(exception, PostgresErrorCodes.UniqueViolation, constraint);

    /// <param name="constraint">The foreign key that must have been violated, or null for any.</param>
    public static bool IsForeignKeyViolation(this DbUpdateException exception, string? constraint = null)
        => Violates(exception, PostgresErrorCodes.ForeignKeyViolation, constraint);


    private static bool Violates(DbUpdateException exception, string sqlState, string? constraint)
    {
        return exception.InnerException is PostgresException postgres
            && postgres.SqlState == sqlState
            && (constraint is null || postgres.ConstraintName == constraint);
    }
}
