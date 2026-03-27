namespace DataViewer.Domain.Exceptions;

/// <summary>
/// Thrown when the audit-logging subsystem fails to persist an audit entry for a
/// completed data-access or administrative operation.
/// </summary>
/// <remarks>
/// The DataViewer specification mandates 100% audit completeness (G-03): every view
/// and search operation must be written to the audit log before the response is
/// returned. When the write fails, this exception is raised so that the API layer can
/// respond with an appropriate error rather than silently skipping the audit record.
/// <para>
/// <see cref="AuditedAction"/> carries a short identifier of the operation that
/// triggered the audit write (e.g. <c>"SearchTransactions"</c>, <c>"ViewTransaction"</c>),
/// which is included in structured logs to assist with post-incident investigation.
/// </para>
/// <para>
/// This exception should never be swallowed or demoted to a warning; a suppressed
/// audit failure violates the auditability guarantee and must surface as a fault.
/// </para>
/// </remarks>
public sealed class AuditFailureException : DomainException
{
    /// <summary>
    /// Initialises the exception with a description of the audit failure and
    /// the action that could not be audited.
    /// </summary>
    /// <param name="message">Human-readable description of why the audit write failed.</param>
    /// <param name="auditedAction">
    /// Short identifier of the operation whose audit record could not be persisted
    /// (e.g. <c>"SearchTransactions"</c>, <c>"ViewTransaction"</c>,
    /// <c>"CreateCredentialProfile"</c>).
    /// </param>
    public AuditFailureException(string message, string auditedAction)
        : base(message)
    {
        AuditedAction = auditedAction;
    }

    /// <summary>
    /// Initialises the exception with a description of the audit failure,
    /// the action that could not be audited, and the underlying cause.
    /// </summary>
    /// <param name="message">Human-readable description of why the audit write failed.</param>
    /// <param name="auditedAction">
    /// Short identifier of the operation whose audit record could not be persisted.
    /// </param>
    /// <param name="innerException">
    /// The lower-level exception (e.g. a database connectivity error) that
    /// caused the audit write to fail.
    /// </param>
    public AuditFailureException(string message, string auditedAction, Exception innerException)
        : base(message, innerException)
    {
        AuditedAction = auditedAction;
    }

    // ── Audit-specific properties ────────────────────────────────────────────

    /// <summary>
    /// Short identifier of the data-access or administrative action that triggered
    /// the audit write which subsequently failed.
    /// <para>
    /// Matches the string representation of the corresponding
    /// <c>AuditActionType</c> enum member (e.g. <c>"SearchTransactions"</c>,
    /// <c>"ViewTransaction"</c>), or a freeform label for infrastructure-level
    /// operations that do not map to a specific enum value.
    /// </para>
    /// </summary>
    public string AuditedAction { get; }
}
