using DataViewer.Core.Domain;

namespace DataViewer.Core.Interfaces;

/// <summary>Generates and validates JWT access tokens and refresh tokens (FR-01, FR-03).</summary>
public interface ITokenService
{
    /// <summary>Creates a signed JWT access token for the given user.</summary>
    string GenerateAccessToken(User user);

    /// <summary>Creates a cryptographically random refresh-token string and returns its SHA-256 hash.</summary>
    (string rawToken, string tokenHash) GenerateRefreshToken();

    /// <summary>
    /// Validates the access token and returns the user ID claim, or null if invalid/expired.
    /// </summary>
    int? GetUserIdFromToken(string token);
}
