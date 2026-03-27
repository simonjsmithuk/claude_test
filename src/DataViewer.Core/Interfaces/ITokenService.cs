#nullable enable

using DataViewer.Domain.Entities;

namespace DataViewer.Core.Interfaces;

/// <summary>
/// Generates and validates JWT access tokens and refresh tokens (FR-01, FR-03).
/// </summary>
/// <remarks>
/// Implementations live in the Infrastructure layer. All user references use
/// <see cref="DataViewer.Domain.Entities.User"/> (Guid primary key) — the stale
/// <c>DataViewer.Core.Domain.User</c> model with int PKs has been retired.
/// </remarks>
public interface ITokenService
{
    /// <summary>Creates a signed JWT access token for the given user.</summary>
    /// <param name="user">The authenticated user for whom the token is being issued.</param>
    /// <returns>A compact serialised JWT string.</returns>
    string GenerateAccessToken(User user);

    /// <summary>
    /// Creates a cryptographically random refresh-token string and returns its SHA-256 hash.
    /// </summary>
    /// <remarks>
    /// The raw token is produced by <c>RandomNumberGenerator.GetBytes(32)</c> (256 bits
    /// of CSPRNG output). The returned <paramref name="rawToken"/> is returned to the
    /// client once and never stored; only <paramref name="tokenHash"/> is persisted.
    /// </remarks>
    /// <returns>
    /// A tuple of the raw opaque token (to return to the client) and its SHA-256 hex
    /// hash (to store in <see cref="RefreshToken.TokenHash"/>).
    /// </returns>
    (string rawToken, string tokenHash) GenerateRefreshToken();

    /// <summary>
    /// Validates the access token and returns the user ID claim as a <see cref="Guid"/>,
    /// or <see langword="null"/> if the token is invalid or expired.
    /// </summary>
    /// <param name="token">The compact serialised JWT string to validate.</param>
    /// <returns>
    /// The <see cref="User.Id"/> encoded in the token's subject claim, or
    /// <see langword="null"/> when validation fails.
    /// </returns>
    Guid? GetUserIdFromToken(string token);
}
