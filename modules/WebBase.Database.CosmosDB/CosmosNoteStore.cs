using System.Net;
using Microsoft.Azure.Cosmos;

namespace WebBase.Database.CosmosDB;

public class CosmosNoteStore(CosmosClient client, CosmosSettings settings) : INoteStore
{
    private Container Notes => client.GetContainer(settings.DatabaseName, CosmosSettings.Notes);

    /// <summary>
    /// The stored shape. The model is mapped rather than stored as-is so the
    /// time can also be kept in a fixed-width form that sorts correctly as
    /// text: JSON trims trailing zeros ("…13.12Z" would sort after "…13.123Z"),
    /// and Cosmos numbers are doubles, too small to hold ticks exactly.
    /// </summary>
    private record NoteDoc(string Id, Guid UserId, string Text, DateTime CreatedAtUtc, string Created)
    {
        public static NoteDoc From(Note n) =>
            new(n.Id.ToString(), n.UserId, n.Text, n.CreatedAtUtc, n.CreatedAtUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"));
        public Note ToNote() => new() { Id = Guid.Parse(Id), UserId = UserId, Text = Text, CreatedAtUtc = CreatedAtUtc };
    }

    public async Task<IReadOnlyList<Note>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT * FROM n WHERE n.userId = @user ORDER BY n.created DESC")
            .WithParameter("@user", userId);
        var notes = new List<Note>();
        using var pages = Notes.GetItemQueryIterator<NoteDoc>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId.ToString()) });
        while (pages.HasMoreResults)
            notes.AddRange((await pages.ReadNextAsync(ct)).Select(d => d.ToNote()));
        return notes;
    }

    public Task AddAsync(Note note, CancellationToken ct = default) =>
        Notes.CreateItemAsync(NoteDoc.From(note), new PartitionKey(note.UserId.ToString()), cancellationToken: ct);

    public async Task<bool> RemoveAsync(Guid userId, Guid noteId, CancellationToken ct = default)
    {
        try
        {
            // Looked for only in this user's partition: someone else's note is simply not there.
            await Notes.DeleteItemAsync<NoteDoc>(noteId.ToString(), new PartitionKey(userId.ToString()), cancellationToken: ct);
            return true;
        }
        catch (CosmosException e) when (e.Is(HttpStatusCode.NotFound))
        {
            return false;
        }
    }
}
