namespace DataViewer.Application.DTOs.AdminSettings;

/// <summary>
/// Response payload for the GET /api/admin/settings endpoint.
/// Contains all system settings fields including server-managed metadata.
/// </summary>
/// <remarks>
/// <para>
/// This type is separate from <see cref="SystemSettingsRequestDto"/> specifically to
/// carry <see cref="UpdatedAt"/>, which is a server-managed field populated by the
/// Infrastructure layer and must never be accepted from inbound request payloads.
/// Keeping it on the response DTO only enforces this contract at the type level.
/// </para>
/// <para>
/// Maps from the <c>SystemSettings</c> domain entity. The Infrastructure layer's
/// <c>SaveChanges</c> interceptor is the authoritative writer of <see cref="UpdatedAt"/>.
/// </para>
/// </remarks>
public sealed record SystemSettingsResponseDto
{
    /// <summary>
    /// Lifetime of a JWT access token in whole minutes.
    /// </summary>
    public int JwtAccessTokenMinutes { get; init; }

    /// <summary>
    /// Lifetime of a refresh token in whole hours.
    /// </summary>
    public int JwtRefreshTokenHours { get; init; }

    /// <summary>
    /// Maximum size in megabytes of an S3 object body that the API will decompress
    /// and return.
    /// </summary>
    public int BodySizeCapMb { get; init; }

    /// <summary>
    /// Number of consecutive failed login attempts before an account is automatically
    /// locked. A value of <c>0</c> means automatic lockout is disabled.
    /// </summary>
    public int LockoutThreshold { get; init; }

    /// <summary>
    /// UTC timestamp of the most recent administrative update to these settings,
    /// with explicit UTC offset.
    /// </summary>
    /// <remarks>
    /// Server-managed; never sourced from client input. <see cref="DateTimeOffset"/>
    /// is used instead of <see cref="DateTime"/> so the serialised JSON always carries
    /// an explicit <c>+00:00</c> offset suffix.
    /// The underlying <c>SystemSettings</c> domain entity uses <see cref="DateTime"/>;
    /// the mapping layer must convert via
    /// <c>DateTime.SpecifyKind(value, DateTimeKind.Utc)</c> before assigning to a
    /// <see cref="DateTimeOffset"/> to preserve the UTC guarantee on the wire.
    /// </remarks>
    public DateTimeOffset UpdatedAt { get; init; }
}
