using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebBase.Api.Contracts;
using WebBase.Api.Infrastructure;
using WebBase.Api.Services;

namespace WebBase.Api.Controllers;

/// <summary>
/// Who the authors are (docs/AUTHORS.md). Authors manage authors; nobody else
/// can even list them. An author can add anyone who already has an account and
/// can remove anyone but themselves, so at least one author always remains.
/// </summary>
[ApiController]
[Route("api/authors")]
[Authorize, RequireAuthor]
public class AuthorsController(IUserStore users) : ControllerBase
{
    [HttpGet]
    public async Task<List<AuthorDto>> List(CancellationToken ct) =>
        (await users.ListAuthorsAsync(ct)).Select(AuthorDto.From).ToList();

    [HttpPost]
    public async Task<ActionResult<AuthorDto>> Add(AddAuthorRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(AuthService.NormalizeEmail(request.Email), ct);
        if (user == null)
            return NotFound(new ErrorResponse("Nobody has an account with that email yet. Ask them to sign up first, then add them."));
        if (!user.IsAuthor)
        {
            user.IsAuthor = true;
            await users.UpdateAsync(user, ct);
        }
        return AuthorDto.From(user); // adding an author twice is not an error
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        if (id == User.GetUserId())
            return BadRequest(new ErrorResponse("You can't remove yourself. Ask another author to do it, so the site always has one."));
        var user = await users.FindByIdAsync(id, ct);
        if (user is not { IsAuthor: true }) return NotFound(new ErrorResponse("That person isn't an author."));
        user.IsAuthor = false;
        await users.UpdateAsync(user, ct);
        return NoContent();
    }
}
