using System.ComponentModel.DataAnnotations;

namespace DataViewer.Application.DTOs.Auth;

/// <summary>
/// Request payload for the POST /api/auth/logout endpoint.
/// Identifies the refresh token to revoke so it can no longer be used.
/// </summary>
/// <remarks>
/// Revoking by token value (rather than by user ID alone) allows targeted
/// single-device logout without invalidating all sessions for that user.
/// </remarks>
public sealed record LogoutRequestDto
{
    /// <summary>
    /// The opaque refresh token to revoke.
    /// After a successful logout the token's <c>IsRevoked</c> flag is set to
    /// <see langword="true"/> in the database and the token cannot be used again.
    /// </summary>
    [Required(ErrorMessage = "Refresh token is required.")]
    public string RefreshToken { get; init; } = string.Empty;
}
