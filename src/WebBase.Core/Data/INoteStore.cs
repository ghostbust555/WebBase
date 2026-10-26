using WebBase.Models;

namespace WebBase.Data;

/// <summary>
/// THE EXAMPLE FEATURE's storage (see Models/Note.cs).
///
/// Every method that reads or removes takes the owner's id. That's on
/// purpose: a store for user data cannot be asked for "note 123" without also
/// saying whose, so one person can never reach another's rows by guessing an id.
/// Copy that shape for every store of user data.
///
/// The contract (checked by tests/WebBase.Testing/StoreContractTests.cs): reads return
/// copies, lists are newest first, times are UTC to the millisecond at least,
/// and removing someone else's note behaves exactly like removing a missing one.
/// </summary>
public interface INoteStore
{
    /// <summary>The user's notes, newest first.</summary>
    Task<IReadOnlyList<Note>> ListAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(Note note, CancellationToken ct = default);

    /// <summary>False if there is no such note belonging to this user.</summary>
    Task<bool> RemoveAsync(Guid userId, Guid noteId, CancellationToken ct = default);
}
