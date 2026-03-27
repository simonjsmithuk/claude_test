#nullable enable

namespace DataViewer.Domain.Entities;

using DataViewer.Domain.Enums;

/// <summary>
/// An immutable record of a single auditable action performed within DataViewer.
/// </summary>
/// <remarks>
/// Audit log entries are append-only: they are never updated or hard-deleted via
/// application APIs. This guarantees a complete, tamper-evident history of all
/// data-access and administration operations.
///
/// <para>
/// The <see cref="Parameters"/> field stores a JSON-serialised snapshot of the
/// search filters or request context that was active at the time of the action.
/// Optional fields (<see cref="ResultCount"/>, <see cref="S3ObjectKey"/>,
/// <see cref="ProfileName"/>) are populated only when relevant to the action type.
/// </para>
/// </remarks>
public class AuditLogEntry
{
    /// <summary>Primary key — generated on creation, never reassigned.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The <see cref="User.Id"/> of the user who triggered the action.
    /// Stored as a value (not a navigation property) to ensure log entries survive
    /// even if the user account is subsequently deactivated or removed.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Categorical type of the operation that was performed.
    /// Integer value is persisted; do not reorder <see cref="AuditActionType"/> members.
    /// </summary>
    public AuditActionType ActionType { get; set; }

    /// <summary>
    /// UTC timestamp at which the action occurred, captured server-side before the
    /// response is returned so that every action is recorded regardless of client clock skew.
    /// </summary>
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Originating IPv4 or IPv6 address of the request, extracted from the
    /// <c>X-Forwarded-For</c> header (when behind a proxy) or the direct connection.
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialised representation of the query parameters or request body
    /// associated with the action (e.g. search filters for <c>SearchTransactions</c>).
    /// <see langword="null"/> for actions that carry no queryable parameters
    /// (e.g. <c>Login</c>, <c>Logout</c>).
    /// </summary>
    public string? Parameters { get; set; }

    /// <summary>
    /// Number of records returned by a search or listing operation.
    /// <see langword="null"/> for non-search actions.
    /// </summary>
    public int? ResultCount { get; set; }

    /// <summary>
    /// The S3 object key that was accessed for single-record view operations.
    /// <see langword="null"/> for all other action types.
    /// </summary>
    public string? S3ObjectKey { get; set; }

    /// <summary>
    /// The display name of the <see cref="CredentialProfile"/> that was active
    /// at the time of the action, snapshotted here so that the log remains meaningful
    /// even after the profile is renamed or deleted.
    /// <see langword="null"/> for non-S3 actions (e.g. <c>Login</c>).
    /// </summary>
    public string? ProfileName { get; set; }
}
