using System.ComponentModel.DataAnnotations;

namespace DataViewer.Application.DTOs.Auth;

/// <summary>
/// Request payload for the POST /api/auth/refresh endpoint.
/// Provides the opaque refresh token used to obtain a new JWT access token.
/// </summary>
public sealed record RefreshTokenRequestDto
{
    /// <summary>
    /// The opaque refresh token previously issued by POST /api/auth/login
    /// or a prior POST /api/auth/refresh call.
    /// The server computes its SHA-256 hash to look up and validate the stored record.
    /// </summary>
    [Required(ErrorMessage = "Refresh token is required.")]
    public string RefreshToken { get; init; } = string.Empty;
}
