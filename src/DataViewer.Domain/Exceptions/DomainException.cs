namespace DataViewer.Domain.Exceptions;

/// <summary>
/// Base class for all domain-layer exceptions in DataViewer.
/// Extend this class for every business-rule violation rather than throwing
/// raw BCL exceptions from domain or application code.
/// </summary>
/// <remarks>
/// Outer layers (API, Infrastructure) catch <see cref="DomainException"/> to map
/// domain faults to appropriate HTTP status codes without leaking implementation details.
/// </remarks>
public abstract class DomainException : Exception
{
    /// <summary>Initialises the exception with a descriptive message.</summary>
    protected DomainException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises the exception with a descriptive message and the lower-level
    /// exception that caused this domain fault.
    /// </summary>
    protected DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
