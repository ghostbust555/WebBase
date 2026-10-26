namespace WebBase.Models;

/// <summary>
/// One account. Every way of signing in ends at one of these rows.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Always stored trimmed and lower-cased, so lookups never miss on case.</summary>
    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Null for an account that has only ever signed in with Google: there is no
    /// password to guess because there is no password.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Google's permanent id for this person, when they have signed in with
    /// Google. The account, not the address: an address can change on a Google
    /// account and this cannot.
    /// </summary>
    public string? GoogleSubject { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
