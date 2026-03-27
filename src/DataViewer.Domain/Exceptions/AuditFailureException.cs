namespace DataViewer.Domain.Exceptions;

/// <summary>
/// Thrown when the audit-logging subsystem fails to persist an audit entry for a
/// completed data-access or administrative operation.
/// </summary>
/// <remarks>
/// The DataViewer specification mandates 100 % audit completeness (Goal G-03):
/// every view and search operation must be written to the audit log before the
/// API response is returned to the caller. When the write fails for any reason
/// (database connectivity, constraint violation, etc.) this exception is raised
/// so that the API layer responds with an explicit fault rather than silently
/// omitting the audit record.
/// <para>
/// <b>This exception must never be swallowed or silently demoted to a warning.</b>
/// A suppressed audit failure violates the auditability guarantee of the system
/// and creates a compliance gap.
/// </para>
/// <para>
/// <see cref="AuditedAction"/> carries a short identifier of the operation that
/// triggered the audit write (e.g. <c>"SearchTransactions"</c>,
/// <c>"ViewTransaction"</c>). It is included in structured logs to assist with
/// post-incident investigation and reconciliation of the audit trail.
/// </para>
/// </remarks>
public sealed class AuditFailureException : DomainException
{
    /// <summary>
    /// Initialises the exception with a description of the audit failure and the
    /// action whose audit record could not be persisted.
    /// </summary>
    /// <param name="message">
    /// Human-readable description of why the audit write failed.
    /// </param>
    /// <param name="auditedAction">
    /// Short identifier of the operation whose audit entry could not be written
    /// (e.g. <c>"SearchTransactions"</c>, <c>"ViewTransaction"</c>,
    /// <c>"CreateCredentialProfile"</c>).
    /// Should match the string representation of the corresponding
    /// <c>AuditActionType</c> enum value where one exists.
    /// </param>
    public AuditFailureException(string message, string auditedAction)
        : base(message)
    {
        AuditedAction = auditedAction;
    }

    /// <summary>
    /// Initialises the exception with a description of the audit failure, the
    /// action that could not be audited, and the underlying root cause.
    /// </summary>
    /// <param name="message">
    /// Human-readable description of why the audit write failed.
    /// </param>
    /// <param name="auditedAction">
    /// Short identifier of the operation whose audit entry could not be written.
    /// </param>
    /// <param name="innerException">
    /// The lower-level exception (e.g. a database connectivity or EF Core
    /// exception) that caused the audit write to fail.
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
    /// This value matches the string representation of the corresponding
    /// <c>AuditActionType</c> enum member (e.g. <c>"SearchTransactions"</c>,
    /// <c>"ViewTransaction"</c>), or a freeform label for infrastructure-level
    /// operations that do not map directly to a specific enum value.
    /// </para>
    /// </summary>
    public string AuditedAction { get; }
}
