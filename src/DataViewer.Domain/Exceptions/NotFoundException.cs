namespace DataViewer.Domain.Exceptions;

/// <summary>
/// Thrown when a requested resource cannot be located in the backing store or
/// on the remote service (e.g. an S3 object key that no longer exists).
/// </summary>
/// <remarks>
/// Maps to <c>HTTP 404 Not Found</c> in the API layer.
/// <para>
/// <see cref="ResourceType"/> and <see cref="Identifier"/> are surfaced in
/// structured log output and — in non-production environments — in the
/// RFC 7807 Problem Details <c>detail</c> field so that callers receive a
/// precise description of exactly which resource was missing without leaking
/// sensitive system information in production responses.
/// </para>
/// <para>
/// Typical <see cref="ResourceType"/> values: <c>"User"</c>,
/// <c>"CredentialProfile"</c>, <c>"AuditEntry"</c>, <c>"S3Object"</c>.
/// </para>
/// </remarks>
public sealed class NotFoundException : DomainException
{
    /// <summary>
    /// Initialises the exception for a resource that could not be found.
    /// </summary>
    /// <param name="resourceType">
    /// Short, human-readable label for the type of resource that was not found
    /// (e.g. <c>"CredentialProfile"</c>, <c>"User"</c>, <c>"S3Object"</c>).
    /// </param>
    /// <param name="identifier">
    /// The key or identifier used to look up the resource — typically a GUID,
    /// a username, or an S3 object key.
    /// </param>
    public NotFoundException(string resourceType, string identifier)
        : base(BuildMessage(resourceType, identifier))
    {
        ResourceType = resourceType;
        Identifier = identifier;
    }

    /// <summary>
    /// Initialises the exception for a resource that could not be found,
    /// preserving the lower-level cause (e.g. an AWS SDK <c>NoSuchKeyException</c>).
    /// </summary>
    /// <param name="resourceType">
    /// Short, human-readable label for the type of resource that was not found.
    /// </param>
    /// <param name="identifier">
    /// The key or identifier used to look up the resource.
    /// </param>
    /// <param name="innerException">The lower-level exception that caused this fault.</param>
    public NotFoundException(string resourceType, string identifier, Exception innerException)
        : base(BuildMessage(resourceType, identifier), innerException)
    {
        ResourceType = resourceType;
        Identifier = identifier;
    }

    // ── Resource-identification properties ───────────────────────────────────

    /// <summary>
    /// Short, human-readable label for the type of resource that was not found
    /// (e.g. <c>"CredentialProfile"</c>, <c>"User"</c>, <c>"S3Object"</c>).
    /// </summary>
    public string ResourceType { get; }

    /// <summary>
    /// The key or identifier that was used to attempt the lookup.
    /// Typically a GUID string, a username, or an S3 object key.
    /// </summary>
    public string Identifier { get; }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string BuildMessage(string resourceType, string identifier) =>
        $"{resourceType} with identifier '{identifier}' was not found.";
}
