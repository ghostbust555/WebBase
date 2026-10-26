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
public class NotesController(INoteStore notes) : ControllerBase
{
    [HttpGet]
    public async Task<List<NoteDto>> List(CancellationToken ct) =>
        (await notes.ListAsync(User.GetUserId(), ct)).Select(NoteDto.From).ToList();

    [HttpPost]
    public async Task<ActionResult<NoteDto>> Create(CreateNoteRequest request)
    {
        var text = (request.Text ?? "").Trim();
        if (text.Length == 0) return BadRequest(new ErrorResponse("Write something first."));
        if (text.Length > 2000) return BadRequest(new ErrorResponse("Notes can be at most 2000 characters."));

        var note = new Note { UserId = User.GetUserId(), Text = text };
        await notes.AddAsync(note);
        return CreatedAtAction(nameof(List), NoteDto.From(note));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id)
    {
        // Someone else's note answers exactly like a missing one.
        return await notes.RemoveAsync(User.GetUserId(), id)
            ? NoContent()
            : NotFound(new ErrorResponse("No such note."));
    }
}
