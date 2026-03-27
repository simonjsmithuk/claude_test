namespace DataViewer.Domain.Exceptions;

/// <summary>
/// Thrown when an operation is attempted by a principal whose credentials are
/// absent, invalid, or expired — or whose account has been locked out due to
/// repeated failed authentication attempts.
/// </summary>
/// <remarks>
/// Maps to <c>HTTP 401 Unauthorized</c> in the API layer.
/// <para>
/// <b>Lockout semantics:</b> when this exception is raised because of a lockout,
/// <see cref="IsAccountLocked"/> is <see langword="true"/> and
/// <see cref="LockoutUntilUtc"/> carries the UTC instant at which the automatic
/// lock expires. A <see langword="null"/> value for <see cref="LockoutUntilUtc"/>
/// alongside <see cref="IsAccountLocked"/> being <see langword="true"/> signals an
/// indefinite administrative lock that must be cleared manually.
/// </para>
/// <para>
/// Use the single-argument constructor for generic authorisation failures (bad
/// password, expired token, etc.) and the lockout overload specifically for
/// account-lockout scenarios so that the API layer can include a
/// <c>Retry-After</c> header in the response.
/// </para>
/// </remarks>
public sealed class UnauthorizedException : DomainException
{
    /// <summary>
    /// Initialises the exception for a generic authorisation failure where the
    /// account is not locked.
    /// </summary>
    /// <param name="message">Human-readable reason for the authorisation failure.</param>
    public UnauthorizedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises the exception for an account-lockout scenario.
    /// Sets <see cref="IsAccountLocked"/> to <see langword="true"/>.
    /// </summary>
    /// <param name="message">Human-readable reason for the authorisation failure.</param>
    /// <param name="lockoutUntilUtc">
    /// UTC timestamp at which the automatic lockout expires, or
    /// <see langword="null"/> for an indefinite administrative lock that
    /// requires manual intervention to clear.
    /// <para>
    /// <see cref="DateTimeOffset"/> is used (rather than <see cref="DateTime"/>)
    /// to carry unambiguous UTC context, matching the
    /// <c>User.LockoutUntil</c> domain-entity property.
    /// </para>
    /// </param>
    public UnauthorizedException(string message, DateTimeOffset? lockoutUntilUtc)
        : base(message)
    {
        IsAccountLocked = true;
        LockoutUntilUtc = lockoutUntilUtc;
    }

    /// <summary>
    /// Initialises the exception for a generic authorisation failure caused by a
    /// lower-level exception (e.g. a JWT-library or hashing error).
    /// </summary>
    /// <param name="message">Human-readable reason for the authorisation failure.</param>
    /// <param name="innerException">The lower-level exception that caused this fault.</param>
    public UnauthorizedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    // ── Lockout properties ───────────────────────────────────────────────────

    /// <summary>
    /// <see langword="true"/> when this exception was raised because the target
    /// account is currently locked out due to repeated failed login attempts.
    /// Always <see langword="false"/> when the single-argument or inner-exception
    /// constructors are used.
    /// </summary>
    public bool IsAccountLocked { get; }

    /// <summary>
    /// UTC timestamp at which the automatic lockout expires and the account will
    /// become accessible again.
    /// <para>
    /// <see langword="null"/> in two cases:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <see cref="IsAccountLocked"/> is <see langword="false"/> — the
    ///       exception is not a lockout fault.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="IsAccountLocked"/> is <see langword="true"/> but the
    ///       lock is indefinite and must be cleared by an administrator.
    ///     </description>
    ///   </item>
    /// </list>
    /// </para>
    /// </summary>
    public DateTimeOffset? LockoutUntilUtc { get; }
}
