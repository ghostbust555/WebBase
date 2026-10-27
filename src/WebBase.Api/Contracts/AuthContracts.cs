namespace WebBase.Api.Contracts;

// What the API accepts and returns. Kept apart from the database models so a
// column can be added without it leaking out to the browser by accident -
// a password hash, for instance.

public record RegisterRequest(string Email, string Password, string? DisplayName);
public record LoginRequest(string Email, string Password);
public record GoogleSignInRequest(string Credential);
public record UpdateProfileRequest(string DisplayName);

public record UserDto(Guid Id, string Email, string DisplayName, bool HasPassword, bool HasGoogle, bool IsAuthor, DateTime CreatedAtUtc)
{
    public static UserDto From(User u) =>
        new(u.Id, u.Email, u.DisplayName, u.PasswordHash != null, u.GoogleSubject != null, u.IsAuthor, u.CreatedAtUtc);
}

public record AuthResponse(string Token, UserDto User);

/// <summary>Every error from this API has this shape, so the client can always show the message.</summary>
public record ErrorResponse(string Error);

public record AuthorDto(Guid Id, string Email, string DisplayName, DateTime CreatedAtUtc)
{
    public static AuthorDto From(User u) => new(u.Id, u.Email, u.DisplayName, u.CreatedAtUtc);
}

public record AddAuthorRequest(string Email);
