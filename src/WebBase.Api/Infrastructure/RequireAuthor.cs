using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WebBase.Api.Contracts;

namespace WebBase.Api.Infrastructure;

/// <summary>
/// Only authors may call this endpoint (docs/AUTHORS.md). Put it next to
/// [Authorize]:
///
///     [Authorize, RequireAuthor]
///
/// The answer comes from the database on every request, never from the token:
/// the token only says who someone was when they signed in, so an author
/// removed a minute ago must be refused a minute ago. One cheap read by id.
/// 403 with { "error": ... } for everyone else; 401 if there's no valid sign-in at all.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireAuthorAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        if (http.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var user = await http.RequestServices.GetRequiredService<IUserStore>()
            .FindByIdAsync(http.User.GetUserId(), http.RequestAborted);
        if (user == null) // a valid token for an account that has since been deleted
            context.Result = new UnauthorizedResult();
        else if (!user.IsAuthor)
            context.Result = new ObjectResult(new ErrorResponse("Only authors can do that.")) { StatusCode = StatusCodes.Status403Forbidden };
    }
}
