using Canhoes.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Canhoes.Api.Startup;

public static class DatabaseSetupRunner
{
    private const int MaxAttempts = 6;

    public static async Task InitializeAsync(
        CanhoesDbContext db,
        ILogger logger,
        string? webRootPath = null,
        CancellationToken ct = default)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await db.Database.EnsureCreatedAsync(ct);
                await DbSchema.EnsureAsync(db);

                ct.ThrowIfCancellationRequested();
                DbSeeder.Seed(db, webRootPath);
                return;
            }
            catch (Exception ex) when (IsTransientSqlFailure(ex) && attempt < MaxAttempts)
            {
                lastError = ex;
                var delay = TimeSpan.FromSeconds(Math.Min(5 * attempt, 30));

                logger.LogWarning(
                    ex,
                    "Transient SQL failure while initializing the database. Retrying startup bootstrap in {DelaySeconds}s (attempt {Attempt}/{MaxAttempts}).",
                    delay.TotalSeconds,
                    attempt,
                    MaxAttempts);

                await Task.Delay(delay, ct);
            }
            catch (Exception ex) when (IsTransientSqlFailure(ex))
            {
                lastError = ex;
                break;
            }
        }

        throw new InvalidOperationException(
            "Database initialization failed after retrying transient SQL connectivity errors.",
            lastError);
    }

    private static bool IsTransientSqlFailure(Exception exception)
    {
        if (exception is TimeoutException)
        {
            return true;
        }

        if (exception is SqlException sqlException)
        {
            return sqlException.Errors.Cast<SqlError>().Any(IsTransientSqlError);
        }

        if (exception is InvalidOperationException invalidOperationException
            && invalidOperationException.Message.Contains("transient failure", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return exception.InnerException is not null && IsTransientSqlFailure(exception.InnerException);
    }

    private static bool IsTransientSqlError(SqlError error)
    {
        // Azure SQL transient connection errors and throttling/failover cases.
        return error.Number is
            4060 or   // Cannot open database requested by the login.
            40197 or  // Service encountered an error processing the request.
            40501 or  // Service busy / throttled.
            40613 or  // Database is not currently available.
            49918 or
            49919 or
            49920 or
            10928 or
            10929;
    }
}
