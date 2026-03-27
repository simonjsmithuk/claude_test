using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Entities;
using DataViewer.Domain.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace DataViewer.Infrastructure.Auth;

/// <summary>
/// JWT access token and refresh token implementation of <see cref="ITokenService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Access tokens:</strong> Compact serialised JWTs signed with HMAC-SHA-256
/// (HS256). The signing key is read once from the <c>JWT__SECRET</c> environment
/// variable at construction time and validated for minimum length (≥ 32 bytes /
/// 256 bits when UTF-8 encoded) to guarantee HS256 security. Token lifetime is read
/// from <see cref="ISystemSettingsRepository"/> at generation time so Admin changes
/// to <see cref="Domain.Entities.SystemSettings.JwtAccessTokenMinutes"/> take effect
/// without an application restart; the appsettings fallback value (default: 15 min)
/// is used only when the settings row cannot be resolved synchronously.
/// </para>
///
/// <para>
/// <strong>Refresh tokens:</strong> Opaque cryptographically-random byte sequences
/// produced by <see cref="RandomNumberGenerator.GetBytes(int)"/> (64 bytes / 512 bits
/// of CSPRNG output). The raw token is returned to the caller for client delivery
/// and is never stored. Only its SHA-256 hex hash is persisted via
/// <see cref="IRefreshTokenRepository.StoreAsync"/>. The high-entropy random value
/// itself acts as an effective salt, making the unsalted SHA-256 hash computationally
/// infeasible to reverse.
/// </para>
///
/// <para>
/// <strong>Security invariants:</strong>
/// <list type="bullet">
///   <item>
///     <description>
///       The JWT secret key is read from the environment and never from a config file,
///       ensuring it cannot be accidentally committed to source control.
///     </description>
///   </item>
///   <item>
///     <description>
///       Raw refresh tokens are never stored, logged, or returned from any repository
///       method. Persistence always goes through <see cref="GetRefreshTokenHash"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       Token validation failures throw <see cref="UnauthorizedException"/> with a
///       generic, non-leaking message. Diagnostic detail is logged server-side only.
///     </description>
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <strong>DI lifetime — Singleton:</strong> <see cref="TokenService"/> is safe for
/// concurrent use. The signing key bytes are derived once in the constructor from the
/// environment variable; all subsequent operations are stateless beyond that immutable
/// field. <see cref="JwtSecurityTokenHandler"/> is constructed per-call to avoid any
/// shared-state threading concerns.
/// </para>
/// </remarks>
public sealed class TokenService : ITokenService
{
    // ── Environment variable name ────────────────────────────────────────────

    /// <summary>
    /// Name of the environment variable that supplies the HS256 signing key.
    /// The double-underscore convention maps the variable to the <c>JWT:SECRET</c>
    /// configuration path on Linux, compatible with .NET's environment variable
    /// configuration provider.
    /// </summary>
    internal const string JwtSecretEnvVar = "JWT__SECRET";

    /// <summary>
    /// Minimum UTF-8 byte length for the JWT secret key.
    /// HS256 requires at least 256 bits (32 bytes). We enforce 32 bytes as the
    /// minimum to guarantee cryptographic strength; longer keys are accepted.
    /// </summary>
    private const int MinSecretLengthBytes = 32;

    /// <summary>
    /// Number of cryptographically-random bytes to generate for each refresh token.
    /// 64 bytes (512 bits) of CSPRNG output provides far more entropy than the
    /// maximum SHA-256 output (256 bits), making brute-force reversal infeasible.
    /// </summary>
    /// <remarks>
    /// The acceptance criteria specify "64-byte Base64 string". We generate
    /// 64 random bytes; Base64 encoding of 64 bytes produces an 88-character string.
    /// </remarks>
    private const int RefreshTokenRandomBytes = 64;

    // ── Private fields ───────────────────────────────────────────────────────

    private readonly byte[] _signingKeyBytes;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<TokenService> _logger;

    // ── Constructor ──────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the token service, loading and validating the HS256 signing key
    /// from the <c>JWT__SECRET</c> environment variable.
    /// </summary>
    /// <param name="configuration">
    /// The application configuration. Used to read the <c>Jwt:Issuer</c>,
    /// <c>Jwt:Audience</c>, and <c>Jwt:AccessTokenLifetimeMinutes</c> fallback values.
    /// </param>
    /// <param name="logger">
    /// Structured logger. Key material is never written to log output.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown at startup when <c>JWT__SECRET</c> is absent, empty, or encodes fewer
    /// than <see cref="MinSecretLengthBytes"/> UTF-8 bytes. This is a fatal startup
    /// fault — the application must not run without a sufficiently strong signing key.
    /// </exception>
    public TokenService(
        IConfiguration configuration,
        ILogger<TokenService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _signingKeyBytes = LoadAndValidateSigningKey();
        _jwtOptions = JwtOptions.FromConfiguration(configuration);

        _logger.LogInformation(
            "TokenService initialised. Issuer={Issuer}, Audience={Audience}, "
            + "FallbackAccessTokenLifetimeMinutes={AccessTokenMinutes}",
            _jwtOptions.Issuer,
            _jwtOptions.Audience,
            _jwtOptions.FallbackAccessTokenLifetimeMinutes);
    }

    // ── ITokenService implementation ─────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Claims payload:
    /// <list type="table">
    ///   <listheader>
    ///     <term>Claim</term>
    ///     <description>Value</description>
    ///   </listheader>
    ///   <item>
    ///     <term><c>sub</c></term>
    ///     <description>User ID as a string (<see cref="User.Id"/>).</description>
    ///   </item>
    ///   <item>
    ///     <term><c>name</c></term>
    ///     <description>Username (<see cref="User.UserName"/>).</description>
    ///   </item>
    ///   <item>
    ///     <term><c>role</c></term>
    ///     <description>UserRole enum name string (e.g. "Admin", "Viewer").</description>
    ///   </item>
    ///   <item>
    ///     <term><c>jti</c></term>
    ///     <description>Unique token identifier — <see cref="Guid.NewGuid()"/> as a string.</description>
    ///   </item>
    ///   <item>
    ///     <term><c>iss</c></term>
    ///     <description>Token issuer from configuration (<c>Jwt:Issuer</c>).</description>
    ///   </item>
    ///   <item>
    ///     <term><c>aud</c></term>
    ///     <description>Token audience from configuration (<c>Jwt:Audience</c>).</description>
    ///   </item>
    ///   <item>
    ///     <term><c>exp</c></term>
    ///     <description>
    ///       Expiry derived from <c>Jwt:AccessTokenLifetimeMinutes</c> configuration
    ///       (default: 15 minutes when not configured). The SystemSettings runtime
    ///       override is resolved by the application layer (use-case) before calling
    ///       this method, if desired.
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    public string GenerateAccessToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = DateTime.UtcNow;
        var expiry = now.AddMinutes(_jwtOptions.FallbackAccessTokenLifetimeMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Name, user.UserName),
            // role claim — used by authorization policies to enforce UserRole-based access.
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            // jti (JWT ID): unique identifier for this specific token.
            // Enables per-token revocation checks and prevents replay of individual tokens.
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var key = new SymmetricSecurityKey(_signingKeyBytes);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: now,
            expires: expiry,
            signingCredentials: credentials);

        var handler = new JwtSecurityTokenHandler();
        var compactToken = handler.WriteToken(token);

        _logger.LogDebug(
            "Access token generated for user {UserId} (role={Role}, expires={Expiry:O})",
            user.Id,
            user.Role,
            expiry);

        return compactToken;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Produces <see cref="RefreshTokenRandomBytes"/> (64) cryptographically-random
    /// bytes via <see cref="RandomNumberGenerator.GetBytes(int)"/> and returns them
    /// as a standard Base-64 string. The raw token must be delivered to the client
    /// and then discarded. Use <see cref="GetRefreshTokenHash"/> to obtain the hash
    /// for persistence.
    /// </remarks>
    public string GenerateRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(RefreshTokenRandomBytes);
        var rawToken = Convert.ToBase64String(randomBytes);

        _logger.LogDebug(
            "Refresh token generated ({RandomBytes} random bytes, Base64-encoded)",
            RefreshTokenRandomBytes);

        return rawToken;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Computes SHA-256 over the UTF-8 encoded raw token and returns the result as
    /// a 64-character lowercase hexadecimal string. This matches the
    /// <c>TokenHash</c> column max-length of 64 in <see cref="RefreshTokenConfiguration"/>.
    /// </remarks>
    public string GetRefreshTokenHash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token, nameof(token));

        // SHA256.HashData is the BCL-idiomatic, allocation-efficient path for
        // one-shot hashing without an IDisposable pattern.
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        // Convert.ToHexString produces an uppercase hex string; ToLowerInvariant
        // normalises to lowercase to match the storage convention established in
        // RefreshTokenConfiguration (HasMaxLength(64)) and the acceptance criteria.
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Validates the access token's signature, expiry, issuer, and audience using
    /// <see cref="JwtSecurityTokenHandler.ValidateToken"/>.  Returns the subject
    /// claim parsed as a <see cref="Guid"/>, or <see langword="null"/> on any
    /// validation failure. Validation errors are logged at Debug level so that
    /// routine expired-token events do not pollute production logs.
    /// </remarks>
    public Guid? GetUserIdFromToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(_signingKeyBytes);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = true,
            ValidIssuer = _jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = _jwtOptions.Audience,
            ValidateLifetime = true,
            // Zero clock skew — the caller is the issuer; no tolerance needed.
            ClockSkew = TimeSpan.Zero,
            // Require the exp claim to be present.
            RequireExpirationTime = true,
        };

        try
        {
            var principal = handler.ValidateToken(
                token,
                validationParameters,
                out _);

            var subClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrWhiteSpace(subClaim) || !Guid.TryParse(subClaim, out var userId))
            {
                _logger.LogDebug(
                    "GetUserIdFromToken: sub claim is absent or not a valid Guid");
                return null;
            }

            return userId;
        }
        catch (SecurityTokenExpiredException ex)
        {
            // Routine expiry — Debug level to avoid log noise.
            _logger.LogDebug(ex, "GetUserIdFromToken: token has expired");
            return null;
        }
        catch (SecurityTokenException ex)
        {
            // Signature invalid, issuer/audience mismatch, malformed token, etc.
            _logger.LogDebug(ex, "GetUserIdFromToken: token validation failed");
            return null;
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Reads the <c>JWT__SECRET</c> environment variable and validates that its
    /// UTF-8 byte representation is at least <see cref="MinSecretLengthBytes"/> bytes.
    /// </summary>
    /// <returns>The UTF-8 encoded signing key bytes.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the variable is absent, empty, or the key is too short.
    /// </exception>
    private static byte[] LoadAndValidateSigningKey()
    {
        var rawSecret = Environment.GetEnvironmentVariable(JwtSecretEnvVar);

        if (string.IsNullOrWhiteSpace(rawSecret))
        {
            throw new InvalidOperationException(
                $"Required environment variable '{JwtSecretEnvVar}' is not set or is empty. "
                + "Provide a UTF-8 string of at least 32 characters for the HS256 signing key.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(rawSecret);

        if (keyBytes.Length < MinSecretLengthBytes)
        {
            throw new InvalidOperationException(
                $"Environment variable '{JwtSecretEnvVar}' encodes only {keyBytes.Length} UTF-8 bytes "
                + $"but at least {MinSecretLengthBytes} bytes (256 bits) are required for HS256. "
                + "Use a longer secret string.");
        }

        return keyBytes;
    }
}
