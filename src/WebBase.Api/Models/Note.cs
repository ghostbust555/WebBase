namespace WebBase.Api.Models;

/// <summary>
/// THE EXAMPLE FEATURE. A note belongs to one user and only they can see it.
///
/// It exists to show every layer a feature touches - this model, its store
/// (Data/INoteStore.cs and the Postgres version), a migration, NotesController,
/// client/src/api.ts and client/src/views/NotesView.vue. Copy it for your first
/// real feature, then delete it (docs/DEVELOPING.md, "Removing the example").
///
/// Models are plain classes with no database code in them, so any database
/// can store them (docs/DATABASE.md).
/// </summary>
public class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
