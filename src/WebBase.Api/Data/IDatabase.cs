namespace WebBase.Api.Data;

/// <summary>
/// The database as a whole, rather than one kind of record: getting it ready
/// at startup and checking it's reachable for /api/health.
/// </summary>
public interface IDatabase
{
    /// <summary>A name for logs, e.g. "postgres".</summary>
    string Provider { get; }

    /// <summary>
    /// Make the database ready for this version of the app: create or upgrade
    /// tables (Postgres: apply migrations), containers, indexes. Runs once at
    /// startup and must be safe to run every time.
    /// </summary>
    Task PrepareAsync(CancellationToken ct = default);

    Task<bool> CanConnectAsync(CancellationToken ct = default);
}

/// <summary>
/// A record would break a uniqueness rule (two accounts with one email).
/// Each store turns its database's own error into this one, so callers
/// don't depend on which database it is.
/// </summary>
public class DuplicateKeyException(string message, Exception? inner = null) : Exception(message, inner);
