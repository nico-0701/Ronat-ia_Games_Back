using Npgsql;
using RonatIa.Games.Application.Abstractions;

namespace RonatIa.Games.Infrastructure.Persistence;

public sealed class NpgsqlExceptionClassifier : IDbExceptionClassifier
{
    public bool IsUniqueViolation(Exception exception, string? constraintName = null)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
                && (constraintName is null || postgres.ConstraintName == constraintName))
            {
                return true;
            }
        }

        return false;
    }
}
