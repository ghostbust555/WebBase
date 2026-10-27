using Google.Cloud.Firestore;
using Grpc.Core;
using static WebBase.Database.Firestore.FirestoreValues;

namespace WebBase.Database.Firestore;

/// <summary>
/// INoteStore on Firestore. Each user's notes live under their own document
/// (users/{id}/notes), so a note can only be found by naming its owner.
/// </summary>
public class FirestoreNoteStore(FirestoreDb db) : INoteStore
{
    private CollectionReference NotesOf(Guid userId) =>
        db.Collection(FirestoreNames.Users).Document(userId.ToString()).Collection(FirestoreNames.Notes);

    public async Task<IReadOnlyList<Note>> ListAsync(Guid userId, CancellationToken ct = default) =>
        (await NotesOf(userId).OrderByDescending("createdAtUtc").GetSnapshotAsync(ct))
            .Select(d => new Note
            {
                Id = Guid.Parse(d.Id), UserId = userId,
                Text = d.GetValue<string>("text"), CreatedAtUtc = TimeOf(d, "createdAtUtc"),
            })
            .ToList();

    public Task AddAsync(Note note, CancellationToken ct = default) =>
        NotesOf(note.UserId).Document(note.Id.ToString())
            .CreateAsync(new Dictionary<string, object> { ["text"] = note.Text, ["createdAtUtc"] = Time(note.CreatedAtUtc) }, ct);

    public async Task<bool> RemoveAsync(Guid userId, Guid noteId, CancellationToken ct = default)
    {
        try
        {
            // Looked for only under this user: someone else's note is simply not there.
            await NotesOf(userId).Document(noteId.ToString()).DeleteAsync(Precondition.MustExist, ct);
            return true;
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.NotFound)
        {
            return false;
        }
    }
}
