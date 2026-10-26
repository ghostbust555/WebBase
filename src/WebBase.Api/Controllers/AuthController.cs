using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebBase.Api.Contracts;
using WebBase.Api.Infrastructure;
using WebBase.Api.Services;

namespace WebBase.Api.Controllers;

/// <summary>
/// Signing up, signing in, and "who am I". Everything under /api/auth.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth, TokenService tokens, IGoogleIdentity google, IUserStore users) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var result = await auth.RegisterAsync(request.Email, request.Password, request.DisplayName);
        return result.User == null ? BadRequest(new ErrorResponse(result.Error!)) : SignedIn(result.User);
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var result = await auth.LoginAsync(request.Email, request.Password);
        return result.User == null ? Unauthorized(new ErrorResponse(result.Error!)) : SignedIn(result.User);
    }

    /// <summary>
    /// The credential Google's button handed the browser. Checked with Google's
    /// keys, never trusted: anybody can POST anything here.
    /// </summary>
    [HttpPost("google")]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult<AuthResponse>> Google(GoogleSignInRequest request, CancellationToken ct)
    {
        if (!google.Enabled) return NotFound(new ErrorResponse("Google sign-in is not set up here."));

        var account = await google.VerifyAsync(request.Credential, ct);
        if (account == null) return Unauthorized(new ErrorResponse("That Google sign-in could not be confirmed."));

        var result = await auth.SignInWithGoogleAsync(account);
        return result.User == null ? BadRequest(new ErrorResponse(result.Error!)) : SignedIn(result.User);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me()
    {
        var user = await users.FindByIdAsync(User.GetUserId());
        // A valid token for an account that has since been deleted.
        return user == null ? Unauthorized() : UserDto.From(user);
    }

    [HttpPut("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> UpdateMe(UpdateProfileRequest request)
    {
        var user = await auth.UpdateDisplayNameAsync(User.GetUserId(), request.DisplayName);
        return user == null ? Unauthorized() : UserDto.From(user);
    }

    /// <summary>
    /// A fresh token for whoever is signed in, so a sign-in slides forward
    /// instead of running out. The client calls it once its token is half used.
    /// </summary>
    [HttpPost("refresh")]
    [Authorize]
    public async Task<ActionResult<AuthResponse>> Refresh()
    {
        var user = await users.FindByIdAsync(User.GetUserId());
        return user == null ? Unauthorized() : SignedIn(user);
    }

    private AuthResponse SignedIn(User user) => new(tokens.CreateToken(user), UserDto.From(user));
}
