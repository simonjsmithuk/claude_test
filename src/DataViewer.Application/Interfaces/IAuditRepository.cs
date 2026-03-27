namespace DataViewer.Application.Interfaces;

using DataViewer.Domain.Entities;

/// <summary>
/// Provides append-only persistence of audit log entries (Product Spec § G-03).
/// </summary>
/// <remarks>
/// By design, this repository exposes only an insert operation — no update, delete,
/// or soft-delete methods are present. This enforces the tamper-evident, append-only
/// audit trail requirement: once written, an <see cref="AuditLogEntry"/> is permanent.
///
/// <para>
/// The audit write must be committed to the database before the corresponding
/// API response is returned to the client, guaranteeing that 100% of view and
/// search operations appear in the audit log (Product Spec § G-03).
/// </para>
///
/// <para>
/// Implementations must not throw for transient database errors in a way that
/// would silently swallow missing audit records. Instead, transient failures
/// should propagate as <see cref="DataViewer.Domain.Exceptions.AuditFailureException"/>
/// so the caller can decide whether to fail the entire request or apply a
/// compensating strategy (e.g. out-of-band retry queue).
/// </para>
/// </remarks>
public interface IAuditRepository
{
    /// <summary>
    /// Persists a new <see cref="AuditLogEntry"/> to the backing store.
    /// </summary>
    /// <param name="entry">
    /// The fully-populated audit log entry to insert.
    /// Must have been created via <see cref="AuditLogEntry.CreateForUser"/> or
    /// <see cref="AuditLogEntry.CreateForSystem"/> to satisfy domain invariants.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the entry has been committed.</returns>
    /// <exception cref="DataViewer.Domain.Exceptions.AuditFailureException">
    /// Thrown when the entry cannot be persisted after exhausting retry attempts,
    /// to distinguish audit failures from general data-access errors.
    /// </exception>
    Task InsertAsync(AuditLogEntry entry, CancellationToken cancellationToken);
}
