namespace DataViewer.Application.Interfaces;

using DataViewer.Domain.Enums;
using DataViewer.Domain.ValueObjects;

/// <summary>
/// Writes audit log entries for all auditable operations performed within DataViewer.
/// </summary>
/// <remarks>
/// <para>
/// This service is the single point of entry for all audit writes. Every call to a
/// public method issues exactly one <see cref="IAuditRepository.InsertAsync"/> on an
/// independent database scope (via <see cref="IAuditRepository"/>), guaranteeing that
/// 100 % of view and search operations appear in the audit log before the API response
/// is returned to the caller (Product Spec § G-03, ADR-009).
/// </para>
///
/// <para>
/// <strong>Audit-first contract (§ G-03):</strong>
/// Callers <em>must</em> invoke the appropriate <c>LogXxxAsync</c> method and
/// <c>await</c> its completion <em>before</em> returning data to the client.  If the
/// method throws an <see cref="DataViewer.Domain.Exceptions.AuditFailureException"/>,
/// the caller <em>must not</em> return the requested data — this enforces the
/// auditability guarantee.
/// </para>
///
/// <para>
/// <strong>Exception contract:</strong>
/// If <see cref="IAuditRepository.InsertAsync"/> fails with an
/// <see cref="DataViewer.Domain.Exceptions.AuditFailureException"/> it is propagated
/// as-is. Any other unexpected exception is wrapped in a new
/// <see cref="DataViewer.Domain.Exceptions.AuditFailureException"/> so the caller
/// always receives a single typed domain exception for any audit-subsystem failure.
/// <see cref="OperationCanceledException"/> is <em>never</em> wrapped — it propagates
/// unchanged so cooperative cancellation works correctly.
/// </para>
///
/// <para>
/// <strong>IP address trust:</strong>
/// All methods accept <paramref name="ipAddress"/> as a pre-validated
/// <c>string?</c>. The caller (typically an ASP.NET Core action filter) is responsible
/// for validating that the address was not forged via <c>X-Forwarded-For</c> spoofing
/// before passing it here. Passing an unvalidated <c>X-Forwarded-For</c> value is an
/// OWASP A05 violation.
/// </para>
///
/// <para>
/// <strong>Sensitive fields:</strong>
/// Filter parameters are serialised to JSON via <c>System.Text.Json</c> using a
/// dedicated projection type (<c>SearchAuditParameters</c>) that explicitly excludes
/// fields that must not appear in the audit log. Neither raw user input strings nor
/// credential values are ever concatenated into the JSON — this prevents log injection
/// (OWASP A03).
/// </para>
///
/// <para>
/// <strong>Lifetime:</strong>
/// Implementations must be registered as <em>Scoped</em>, not Singleton, because they
/// depend on <see cref="IAuditRepository"/> which is itself Scoped (it holds a reference
/// to the request-scoped <see cref="Microsoft.EntityFrameworkCore.DbContext"/> for read
/// operations). Registering as Singleton would create a captive dependency.
/// </para>
/// </remarks>
public interface IAuditService
{
    /// <summary>
    /// Writes an audit log entry for a transaction metadata search operation.
    /// </summary>
    /// <param name="userId">
    /// The <see cref="DataViewer.Domain.Entities.User.Id"/> of the user performing
    /// the search. Must not be <see cref="Guid.Empty"/>.
    /// </param>
    /// <param name="ipAddress">
    /// Pre-validated originating IP address of the HTTP request, or
    /// <see langword="null"/> when the address is unavailable.
    /// </param>
    /// <param name="filter">
    /// The search filter criteria applied to the S3 transaction listing.
    /// Serialised to JSON (sensitive fields excluded) and stored in the
    /// <c>Parameters</c> column of the audit log entry.
    /// </param>
    /// <param name="resultCount">
    /// Total number of transaction records returned by the search.
    /// Stored in the <c>ResultCount</c> column.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the entry has been committed.</returns>
    /// <exception cref="DataViewer.Domain.Exceptions.AuditFailureException">
    /// Thrown when the audit entry cannot be persisted. Callers must not return
    /// search results if this exception is thrown.
    /// </exception>
    Task LogSearchAsync(
        Guid userId,
        string? ipAddress,
        SearchFilter filter,
        int resultCount,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes an audit log entry for a single transaction record view operation.
    /// </summary>
    /// <param name="userId">
    /// The <see cref="DataViewer.Domain.Entities.User.Id"/> of the user viewing
    /// the record. Must not be <see cref="Guid.Empty"/>.
    /// </param>
    /// <param name="ipAddress">
    /// Pre-validated originating IP address of the HTTP request, or
    /// <see langword="null"/> when the address is unavailable.
    /// </param>
    /// <param name="s3ObjectKey">
    /// The S3 object key of the transaction record that was accessed.
    /// Stored in the <c>S3ObjectKey</c> column.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the entry has been committed.</returns>
    /// <exception cref="DataViewer.Domain.Exceptions.AuditFailureException">
    /// Thrown when the audit entry cannot be persisted. Callers must not return
    /// the transaction detail if this exception is thrown.
    /// </exception>
    Task LogViewAsync(
        Guid userId,
        string? ipAddress,
        string s3ObjectKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes an audit log entry for a credential profile management operation.
    /// </summary>
    /// <param name="userId">
    /// The <see cref="DataViewer.Domain.Entities.User.Id"/> of the admin user
    /// performing the operation. Must not be <see cref="Guid.Empty"/>.
    /// </param>
    /// <param name="ipAddress">
    /// Pre-validated originating IP address of the HTTP request, or
    /// <see langword="null"/> when the address is unavailable.
    /// </param>
    /// <param name="actionType">
    /// The specific credential action being audited. Must be one of:
    /// <see cref="AuditActionType.CreateCredentialProfile"/>,
    /// <see cref="AuditActionType.UpdateCredentialProfile"/>,
    /// <see cref="AuditActionType.DeleteCredentialProfile"/>,
    /// <see cref="AuditActionType.TestCredentialProfile"/>, or
    /// <see cref="AuditActionType.ActivateCredentialProfile"/>.
    /// </param>
    /// <param name="profileName">
    /// Display name of the affected credential profile, snapshotted at the time
    /// of the operation. Stored in the <c>ProfileName</c> column so the audit
    /// entry remains meaningful if the profile is later renamed or deleted.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the entry has been committed.</returns>
    /// <exception cref="DataViewer.Domain.Exceptions.AuditFailureException">
    /// Thrown when the audit entry cannot be persisted. Callers must not complete
    /// the credential operation if this exception is thrown.
    /// </exception>
    Task LogCredentialActionAsync(
        Guid userId,
        string? ipAddress,
        AuditActionType actionType,
        string profileName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes an audit log entry for an authentication event.
    /// </summary>
    /// <param name="userId">
    /// The <see cref="DataViewer.Domain.Entities.User.Id"/> of the user involved
    /// in the authentication event. For <see cref="AuditActionType.AccountLocked"/>
    /// events triggered by a background/system process with no authenticated
    /// principal, pass <see cref="Guid.Empty"/> — the service will route to the
    /// <see cref="DataViewer.Domain.Entities.AuditLogEntry.CreateForSystem"/> factory
    /// and record the action as system-initiated.
    /// </param>
    /// <param name="ipAddress">
    /// Pre-validated originating IP address of the HTTP request, or
    /// <see langword="null"/> when the event has no HTTP context (e.g. a background
    /// lockout job).
    /// </param>
    /// <param name="actionType">
    /// The authentication event being audited. Must be one of:
    /// <see cref="AuditActionType.Login"/>, <see cref="AuditActionType.Logout"/>,
    /// <see cref="AuditActionType.LoginFailed"/>, or
    /// <see cref="AuditActionType.AccountLocked"/>.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the entry has been committed.</returns>
    /// <exception cref="DataViewer.Domain.Exceptions.AuditFailureException">
    /// Thrown when the audit entry cannot be persisted.
    /// </exception>
    Task LogAuthAsync(
        Guid userId,
        string? ipAddress,
        AuditActionType actionType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes an audit log entry for an administrative action (system settings, user management).
    /// </summary>
    /// <param name="userId">
    /// The <see cref="DataViewer.Domain.Entities.User.Id"/> of the admin user
    /// performing the operation. Must not be <see cref="Guid.Empty"/>.
    /// </param>
    /// <param name="ipAddress">
    /// Pre-validated originating IP address of the HTTP request, or
    /// <see langword="null"/> when the address is unavailable.
    /// </param>
    /// <param name="actionType">
    /// The administrative action being audited. Must be one of:
    /// <see cref="AuditActionType.UpdateSystemSettings"/>,
    /// <see cref="AuditActionType.UpdateUserRole"/>, or
    /// <see cref="AuditActionType.UnlockAccount"/>.
    /// </param>
    /// <param name="details">
    /// Human-readable details string describing the specific changes made.
    /// Stored in the <c>Parameters</c> column of the audit log entry.
    /// Example: "AccessTokenMinutes=15, RefreshTokenHours=24, BodySizeCapMb=10, LockoutThreshold=5"
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when the entry has been committed.</returns>
    /// <exception cref="DataViewer.Domain.Exceptions.AuditFailureException">
    /// Thrown when the audit entry cannot be persisted. Callers must not complete
    /// the administrative operation if this exception is thrown.
    /// </exception>
    Task LogAdminActionAsync(
        Guid userId,
        string? ipAddress,
        AuditActionType actionType,
        string details,
        CancellationToken cancellationToken);
}
