using Microsoft.AspNetCore.Identity;

namespace WebBase.Api.Services;

/// <summary>The outcome of a sign-in attempt: a user, or a message to show.</summary>
public record AuthResult(User? User, string? Error)
{
    public static AuthResult Ok(User user) => new(user, null);
    public static AuthResult Fail(string error) => new(null, error);
}

/// <summary>
/// The rules for accounts: making them, checking passwords, and matching a
/// Google sign-in to the right person. Knows nothing about HTTP, so it can be
/// tested on its own.
/// </summary>
public class AuthService(AppDbContext db)
{
    public const int MinPasswordLength = 8;

    // ASP.NET's own hasher: PBKDF2 with a per-password salt, and it can tell us
    // when a stored hash should be upgraded to stronger settings.
    private static readonly PasswordHasher<User> Hasher = new();

    public static string NormalizeEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();

    public async Task<AuthResult> RegisterAsync(string email, string password, string? displayName)
    {
        email = NormalizeEmail(email);
        if (!email.Contains('@') || email.Length > 320) return AuthResult.Fail("Enter a valid email address.");
        if ((password ?? "").Length < MinPasswordLength)
            return AuthResult.Fail($"Passwords need at least {MinPasswordLength} characters.");
        if (await db.Users.AnyAsync(u => u.Email == email))
            return AuthResult.Fail("That email already has an account. Try signing in.");

        var user = new User { Email = email, DisplayName = CleanName(displayName, email) };
        user.PasswordHash = Hasher.HashPassword(user, password!);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return AuthResult.Ok(user);
    }

    public async Task<AuthResult> LoginAsync(string email, string password)
    {
        email = NormalizeEmail(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        // One message for both "no such account" and "wrong password", so the
        // login form can't be used to find out who has an account.
        const string wrong = "That email and password don't match.";
        if (user?.PasswordHash == null) return AuthResult.Fail(wrong);

        var verdict = Hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? "");
        if (verdict == PasswordVerificationResult.Failed) return AuthResult.Fail(wrong);
        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, password!);
            await db.SaveChangesAsync();
        }
        return AuthResult.Ok(user);
    }

    /// <summary>
    /// Sign in with a Google account the person has just proved they own.
    ///
    /// 1. Seen this Google account before: it's them, whatever its email says now.
    /// 2. The (verified) email already has an account here: same person, new way
    ///    in. Link them; their password keeps working.
    /// 3. Neither: a new account with no password.
    ///
    /// An unverified Google email is refused rather than matched - otherwise
    /// someone could claim another person's account by typing their address.
    /// </summary>
    public async Task<AuthResult> SignInWithGoogleAsync(GoogleAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.Subject)) return AuthResult.Fail("Google did not say who that was.");

        var existing = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == account.Subject);
        if (existing != null) return AuthResult.Ok(existing);

        var email = NormalizeEmail(account.Email);
        if (email.Length == 0 || !account.EmailVerified)
            return AuthResult.Fail("That Google account has no confirmed email address.");

        var byEmail = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (byEmail != null)
        {
            byEmail.GoogleSubject = account.Subject;
            await db.SaveChangesAsync();
            return AuthResult.Ok(byEmail);
        }

        var user = new User { Email = email, GoogleSubject = account.Subject, DisplayName = CleanName(account.Name, email) };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return AuthResult.Ok(user);
    }

    public async Task<User?> UpdateDisplayNameAsync(Guid userId, string displayName)
    {
        var user = await db.Users.FindAsync(userId);
        if (user == null) return null;
        user.DisplayName = CleanName(displayName, user.Email);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Trimmed, at most 80 characters, and never empty.</summary>
    private static string CleanName(string? name, string email)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) trimmed = email.Split('@')[0];
        return trimmed.Length > 80 ? trimmed[..80] : trimmed;
    }
}
