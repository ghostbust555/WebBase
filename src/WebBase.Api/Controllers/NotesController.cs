using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebBase.Api.Contracts;
using WebBase.Api.Infrastructure;

namespace WebBase.Api.Controllers;

/// <summary>
/// THE EXAMPLE FEATURE (see Models/Note.cs). A signed-in user's own notes.
///
/// The one rule every endpoint here follows, and every feature you add should
/// too: filter by the signed-in user's id, so nobody can read or delete another
/// person's rows by guessing an id.
/// </summary>
[ApiController]
[Route("api/notes")]
[Authorize]
public class NotesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<NoteDto>> List() =>
        await db.Notes
            .Where(n => n.UserId == User.GetUserId())
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => new NoteDto(n.Id, n.Text, n.CreatedAtUtc))
            .ToListAsync();

    [HttpPost]
    public async Task<ActionResult<NoteDto>> Create(CreateNoteRequest request)
    {
        var text = (request.Text ?? "").Trim();
        if (text.Length == 0) return BadRequest(new ErrorResponse("Write something first."));
        if (text.Length > 2000) return BadRequest(new ErrorResponse("Notes can be at most 2000 characters."));

        var note = new Note { UserId = User.GetUserId(), Text = text };
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(List), NoteDto.From(note));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        // Someone else's note answers exactly like a missing one.
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == User.GetUserId());
        if (note == null) return NotFound(new ErrorResponse("No such note."));

        db.Notes.Remove(note);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
