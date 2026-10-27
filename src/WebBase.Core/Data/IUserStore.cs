using WebBase.Models;

namespace WebBase.Data;

/// <summary>
/// Where accounts are kept. The rest of the app knows only this interface,
/// never which database is behind it (docs/DATABASE.md).
///
/// The contract every implementation keeps (tests/WebBase.Testing/StoreContractTests.cs
/// checks it):
/// - Reads return a copy. Changing it saves nothing until <see cref="UpdateAsync"/>.
/// - Emails are compared exactly: callers pass them already normalised
///   (AuthService.NormalizeEmail).
/// - No two users share an email, or a Google subject. Breaking either throws
///   <see cref="DuplicateKeyException"/>, even when two requests race.
/// - Times come back in UTC, to the millisecond at least.
/// </summary>
public interface IUserStore
{
    Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> FindByGoogleSubjectAsync(string subject, CancellationToken ct = default);

    /// <summary>True once any account exists. How the first account is told apart (AuthService).</summary>
    Task<bool> AnyAsync(CancellationToken ct = default);

    /// <summary>Every user with <see cref="User.IsAuthor"/>, oldest account first.</summary>
    Task<IReadOnlyList<User>> ListAuthorsAsync(CancellationToken ct = default);

    /// <summary>Saves a new user. Throws <see cref="DuplicateKeyException"/> if the email or Google subject is taken.</summary>
    Task AddAsync(User user, CancellationToken ct = default);

    /// <summary>
    /// Saves every field of an existing user. Throws <see cref="DuplicateKeyException"/>
    /// if the new email or Google subject belongs to someone else, and
    /// <see cref="KeyNotFoundException"/> if the user doesn't exist.
    /// </summary>
    Task UpdateAsync(User user, CancellationToken ct = default);
}
