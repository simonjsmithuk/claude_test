namespace DataViewer.Application.Interfaces;

using DataViewer.Application.DTOs.AuditLogs;
using DataViewer.Application.DTOs.Common;
using DataViewer.Domain.Entities;

/// <summary>
/// Provides append-only persistence of audit log entries and paged read access
/// for the admin audit log endpoint (Product Spec § G-03).
/// </summary>
/// <remarks>
/// By design, this repository exposes only an insert operation and a paged read —
/// no update, delete, or single-record-by-id methods are present.  This enforces
/// the tamper-evident, append-only audit trail requirement: once written, an
/// <see cref="AuditLogEntry"/> is permanent.
///
/// <para>
/// The audit write must be committed to the database <em>before</em> the
/// corresponding API response is returned to the client, guaranteeing that 100 %
/// of view and search operations appear in the audit log (Product Spec § G-03).
/// </para>
///
/// <para>
/// Implementations MUST use a dedicated <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/>
/// scope for <see cref="InsertAsync"/> so that the audit INSERT is committed on its
/// own, independent connection/transaction. This prevents the audit record from being
/// rolled back if the calling request's unit-of-work is later rolled back (e.g. due to
/// a business-rule violation that is handled after the audit entry is written).
/// </para>
///
/// <para>
/// Implementations must not swallow exceptions. Any failure must propagate as
/// <see cref="DataViewer.Domain.Exceptions.AuditFailureException"/> so the caller
/// can decide whether to fail the entire request or apply a compensating strategy.
/// </para>
/// </remarks>
public interface IAuditRepository
{
    /// <summary>
    /// Persists a new <see cref="AuditLogEntry"/> to the backing store using an
    /// independent database scope so the write is not affected by the caller's
    /// ambient transaction.
    /// </summary>
    /// <param name="entry">
    /// The fully-populated audit log entry to insert.
    /// Must have been created via <see cref="AuditLogEntry.CreateForUser"/> or
    /// <see cref="AuditLogEntry.CreateForSystem"/> to satisfy domain invariants.
    /// If <see cref="AuditLogEntry.TimestampUtc"/> is <see cref="DateTime.MinValue"/>
    /// (i.e. default) the repository sets it to <see cref="DateTime.UtcNow"/> before
    /// persisting.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the entry has been committed.</returns>
    /// <exception cref="DataViewer.Domain.Exceptions.AuditFailureException">
    /// Thrown when the entry cannot be persisted, wrapping the root-cause exception,
    /// to distinguish audit failures from general data-access errors.
    /// </exception>
    Task InsertAsync(AuditLogEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a paged, optionally filtered list of <see cref="AuditLogEntry"/>
    /// records for the admin audit log read endpoint.
    /// </summary>
    /// <param name="parameters">
    /// Filter and pagination parameters.  All filter fields are optional; omitting
    /// them produces an unfiltered, time-ordered (newest first) result set.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="PagedResultDto{T}"/> of <see cref="AuditLogEntry"/> containing
    /// the requested page.  The <see cref="PagedResultDto{T}.Data"/> collection is
    /// never <see langword="null"/>; an empty list is returned when no records match
    /// the supplied filters.
    /// </returns>
    /// <exception cref="DataViewer.Domain.Exceptions.AuditFailureException">
    /// Thrown when the query cannot be executed due to a database error, so that
    /// all audit-subsystem failures surface consistently as the same exception type.
    /// </exception>
    Task<PagedResultDto<AuditLogEntry>> GetPagedAsync(
        AuditLogQueryParameters parameters,
        CancellationToken cancellationToken);
}
