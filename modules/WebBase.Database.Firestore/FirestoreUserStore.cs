using Google.Cloud.Firestore;
using static WebBase.Database.Firestore.FirestoreValues;

namespace WebBase.Database.Firestore;

/// <summary>
/// IUserStore on Firestore. Lookups are queries on the users collection; the
/// uniqueness of emails and Google accounts is kept by the "keys" documents,
/// written in the same transaction as the user.
/// </summary>
public class FirestoreUserStore(FirestoreDb db) : IUserStore
{
    private CollectionReference Users => db.Collection(FirestoreNames.Users);
    private CollectionReference Keys => db.Collection(FirestoreNames.Keys);

    private static IEnumerable<string> KeysOf(User u)
    {
        yield return KeyId("email", u.Email);
        if (u.GoogleSubject != null) yield return KeyId("google", u.GoogleSubject);
    }

    private static Dictionary<string, object?> ToDoc(User u) => new()
    {
        ["email"] = u.Email,
        ["displayName"] = u.DisplayName,
        ["passwordHash"] = u.PasswordHash,
        ["googleSubject"] = u.GoogleSubject,
        ["isAuthor"] = u.IsAuthor,
        ["createdAtUtc"] = Time(u.CreatedAtUtc),
    };

    private static User ToUser(DocumentSnapshot d) => new()
    {
        Id = Guid.Parse(d.Id),
        Email = d.GetValue<string>("email"),
        DisplayName = d.GetValue<string>("displayName"),
        PasswordHash = OptionalString(d, "passwordHash"),
        GoogleSubject = OptionalString(d, "googleSubject"),
        IsAuthor = d.TryGetValue<bool>("isAuthor", out var author) && author,
        CreatedAtUtc = TimeOf(d, "createdAtUtc"),
    };

    public async Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await Users.Document(id.ToString()).GetSnapshotAsync(ct);
        return doc.Exists ? ToUser(doc) : null;
    }

    public Task<User?> FindByEmailAsync(string email, CancellationToken ct = default) => FindOneAsync("email", email, ct);

    public Task<User?> FindByGoogleSubjectAsync(string subject, CancellationToken ct = default) => FindOneAsync("googleSubject", subject, ct);

    private async Task<User?> FindOneAsync(string field, string value, CancellationToken ct)
    {
        var found = await Users.WhereEqualTo(field, value).Limit(1).GetSnapshotAsync(ct);
        return found.Count == 0 ? null : ToUser(found[0]);
    }

    public async Task<bool> AnyAsync(CancellationToken ct = default) =>
        (await Users.Limit(1).GetSnapshotAsync(ct)).Count > 0;

    public async Task<IReadOnlyList<User>> ListAuthorsAsync(CancellationToken ct = default) =>
        (await Users.WhereEqualTo("isAuthor", true).GetSnapshotAsync(ct)).Select(ToUser).OrderBy(u => u.CreatedAtUtc).ToList();

    public Task AddAsync(User user, CancellationToken ct = default) =>
        db.RunTransactionAsync(async tx =>
        {
            var userRef = Users.Document(user.Id.ToString());
            if ((await tx.GetSnapshotAsync(userRef, ct)).Exists) throw new DuplicateKeyException("A user with that id already exists.");
            var keys = KeysOf(user).Select(k => Keys.Document(k)).ToList();
            foreach (var key in keys)
                if ((await tx.GetSnapshotAsync(key, ct)).Exists) throw new DuplicateKeyException("That email or Google account belongs to another user.");
            tx.Create(userRef, ToDoc(user));
            foreach (var key in keys) tx.Create(key, KeyDoc(user.Id.ToString()));
        }, cancellationToken: ct);

    public Task UpdateAsync(User user, CancellationToken ct = default) =>
        db.RunTransactionAsync(async tx =>
        {
            var id = user.Id.ToString();
            var userRef = Users.Document(id);
            var old = await tx.GetSnapshotAsync(userRef, ct);
            if (!old.Exists) throw new KeyNotFoundException("That user doesn't exist.");
            var oldKeys = KeysOf(ToUser(old)).ToList();
            var newKeys = KeysOf(user).ToList();
            var added = newKeys.Except(oldKeys).ToList();
            foreach (var key in added)
            {
                var claim = await tx.GetSnapshotAsync(Keys.Document(key), ct);
                if (claim.Exists && claim.GetValue<string>("owner") != id)
                    throw new DuplicateKeyException("That email or Google account belongs to another user.");
            }
            tx.Set(userRef, ToDoc(user));
            foreach (var key in oldKeys.Except(newKeys)) tx.Delete(Keys.Document(key));
            foreach (var key in added) tx.Set(Keys.Document(key), KeyDoc(id));
        }, cancellationToken: ct);
}
