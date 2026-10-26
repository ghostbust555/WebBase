namespace WebBase.Api.Contracts;

// Part of the example feature - see Models/Note.cs.

public record CreateNoteRequest(string Text);

public record NoteDto(Guid Id, string Text, DateTime CreatedAtUtc)
{
    public static NoteDto From(Note n) => new(n.Id, n.Text, n.CreatedAtUtc);
}
