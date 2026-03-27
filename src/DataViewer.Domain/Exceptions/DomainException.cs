namespace DataViewer.Domain.Exceptions;

/// <summary>
/// Abstract base class for all domain-layer exceptions in DataViewer.
/// </summary>
/// <remarks>
/// Every business-rule or invariant violation within the Domain and Application
/// layers should be expressed as a subclass of <see cref="DomainException"/>
/// rather than a raw BCL exception. This design allows outer layers (API,
/// Infrastructure) to catch a single well-known base type and map domain faults
/// to appropriate HTTP status codes or error responses without leaking
/// implementation details to callers.
/// <para>
/// The class is <c>abstract</c> — it is never thrown directly; always throw one
/// of the concrete specialised subtypes that carries the context relevant to the
/// specific failure.
/// </para>
/// </remarks>
public abstract class DomainException : Exception
{
    /// <summary>
    /// Initialises the exception with a human-readable description of the
    /// domain fault.
    /// </summary>
    /// <param name="message">
    /// A clear, non-sensitive description of why the domain rule was violated.
    /// </param>
    protected DomainException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises the exception with a human-readable description of the domain
    /// fault and the lower-level exception that caused it.
    /// </summary>
    /// <param name="message">
    /// A clear, non-sensitive description of why the domain rule was violated.
    /// </param>
    /// <param name="innerException">
    /// The lower-level exception (e.g. a database or SDK exception) that is the
    /// root cause of this domain fault. Preserved for structured logging and
    /// diagnostic tooling; must not be forwarded verbatim to API consumers.
    /// </param>
    protected DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
