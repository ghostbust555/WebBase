namespace WebBase.Api.Models;

/// <summary>
/// THE EXAMPLE FEATURE. A note belongs to one user and only they can see it.
///
/// It exists to show every layer a feature touches - this model, a line in
/// AppDbContext, a migration, NotesController, client/src/api.ts and
/// client/src/views/NotesView.vue. Copy it for your first real feature, then
/// delete it (docs/DEVELOPING.md, "Removing the example").
/// </summary>
public class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
