using DataViewer.Application.DTOs.Common;

namespace DataViewer.Application.DTOs.Auth;

/// <summary>
/// Response payload returned on a successful POST /api/auth/login.
/// Contains the JWT access token, its expiry time, and an opaque refresh token.
/// </summary>
/// <remarks>
/// The <see cref="RefreshToken"/> is a raw opaque value returned once at issuance.
/// Only its SHA-256 hash is stored server-side; the raw value is never retrievable
/// again after this response.
/// <para>
/// ⚠️ Logging: <see cref="RefreshToken"/> is decorated with <see cref="SensitiveDataAttribute"/>.
/// Any request/response logging middleware (e.g. <c>UseHttpLogging()</c>) MUST redact
/// this field before writing to log sinks, otherwise the raw token — whose security
/// model depends on it never being stored in plain text — will appear in log files,
/// negating the server-side SHA-256 hash storage design.
/// </para>
/// </remarks>
public sealed record LoginResponseDto
{
    /// <summary>
    /// A signed compact-serialised JWT access token.
    /// Short-lived; clients must use <see cref="RefreshToken"/> to obtain a new
    /// access token when this one expires.
    /// </summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp at which <see cref="AccessToken"/> expires, with explicit UTC offset.
    /// <see cref="DateTimeOffset"/> is used instead of <see cref="DateTime"/> to guarantee
    /// the serialised wire value always carries an explicit <c>+00:00</c> offset suffix,
    /// making it unambiguous to all JSON consumers regardless of their local timezone.
    /// Clients should treat the token as invalid at or after this instant.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// An opaque cryptographically-random refresh token used to obtain a new
    /// access token via POST /api/auth/refresh.
    /// Longer-lived than <see cref="AccessToken"/>; store securely (e.g. HttpOnly cookie).
    /// </summary>
    /// <remarks>
    /// ⚠️ Sensitive: decorated with <see cref="SensitiveDataAttribute"/> as a signal
    /// to logging middleware to redact this value. See class-level remarks for details.
    /// </remarks>
    [SensitiveData("Raw refresh token must never appear in logs; only its SHA-256 hash is stored server-side.")]
    public string RefreshToken { get; init; } = string.Empty;
}
