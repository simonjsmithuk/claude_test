namespace DataViewer.Domain.Exceptions;

/// <summary>
/// Thrown when an operation is attempted by a principal that lacks the required
/// permissions or whose credentials are absent, invalid, or expired.
/// </summary>
/// <remarks>
/// Maps to HTTP 401 Unauthorized in the API layer.
/// <para>
/// When the account is locked, <see cref="IsAccountLocked"/> is <see langword="true"/>
/// and <see cref="LockoutUntilUtc"/> carries the earliest UTC instant at which the
/// account will automatically unlock (or <see langword="null"/> for an indefinite
/// administrative lock that must be manually cleared).
/// </para>
/// </remarks>
public sealed class UnauthorizedException : DomainException
{
    /// <summary>
    /// Initialises the exception with a reason message.
    /// Use this overload when the account is not locked.
    /// </summary>
    /// <param name="message">Human-readable reason for the authorisation failure.</param>
    public UnauthorizedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises the exception with a reason message and lockout information.
    /// Use this overload when the authentication failure is due to an account lockout.
    /// </summary>
    /// <param name="message">Human-readable reason for the authorisation failure.</param>
    /// <param name="lockoutUntilUtc">
    /// UTC timestamp at which the automatic lockout expires.
    /// <see langword="null"/> for an indefinite administrative lock.
    /// </param>
    public UnauthorizedException(string message, DateTime? lockoutUntilUtc)
        : base(message)
    {
        IsAccountLocked = true;
        LockoutUntilUtc = lockoutUntilUtc;
    }

    /// <summary>
    /// Initialises the exception with a reason message and an inner exception.
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
    /// account is currently locked due to repeated failed login attempts.
    /// </summary>
    public bool IsAccountLocked { get; }

    /// <summary>
    /// UTC timestamp at which the lockout expires and the account will automatically
    /// become accessible again.
    /// <see langword="null"/> when the account is not locked
    /// (<see cref="IsAccountLocked"/> is <see langword="false"/>), or when the lock
    /// is indefinite and must be cleared manually by an administrator.
    /// </summary>
    public DateTime? LockoutUntilUtc { get; }
}
