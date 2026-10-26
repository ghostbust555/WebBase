using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using WebBase.Api.Infrastructure;

namespace WebBase.Api.Services;

/// <summary>One person, as Google describes them.</summary>
public record GoogleAccount(string Subject, string Email, bool EmailVerified, string? Name);

/// <summary>
/// Checks that a credential from Google's sign-in button really came from
/// Google, for this app. An interface so tests can stand in for Google.
/// </summary>
public interface IGoogleIdentity
{
    bool Enabled { get; }
    Task<GoogleAccount?> VerifyAsync(string credential, CancellationToken ct = default);
}

public class GoogleIdentity(IOptionsMonitor<GoogleAuthOptions> options, ILogger<GoogleIdentity> logger) : IGoogleIdentity
{
    public bool Enabled => options.CurrentValue.Enabled;

    public async Task<GoogleAccount?> VerifyAsync(string credential, CancellationToken ct = default)
    {
        var clientId = options.CurrentValue.ClientId;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(credential)) return null;

        try
        {
            // The audience check is the important part: a genuine Google token
            // issued to somebody else's app proves nothing about this one.
            var payload = await GoogleJsonWebSignature.ValidateAsync(credential,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] });
            return new GoogleAccount(payload.Subject, payload.Email ?? "", payload.EmailVerified, payload.Name);
        }
        catch (InvalidJwtException e)
        {
            // Expired, meant for another app, or forged. Never log the token itself.
            logger.LogInformation("Rejected a Google credential: {Reason}", e.Message);
            return null;
        }
    }
}
