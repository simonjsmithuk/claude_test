using DataViewer.Domain.Enums;

namespace DataViewer.Application.DTOs.AuditLogs;

/// <summary>
/// Read model for a single audit log entry returned by GET /api/admin/audit-logs.
/// Maps directly from the <c>AuditLogEntry</c> domain entity; all fields are
/// read-only because audit entries are immutable by design.
/// </summary>
public sealed record AuditLogEntryDto
{
    /// <summary>Unique identifier of the audit log entry.</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The ID of the user who triggered the action.
    /// <see cref="Guid.Empty"/> (<c>00000000-0000-0000-0000-000000000000</c>) indicates
    /// a system-initiated action with no authenticated user context; the frontend
    /// should render this sentinel as "System".
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// The username of the user at the time of the action, if resolvable.
    /// <see langword="null"/> for system-initiated entries (<see cref="UserId"/> == Guid.Empty)
    /// or if the user account has since been removed.
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>
    /// The category of auditable action that was performed.
    /// </summary>
    public AuditActionType ActionType { get; init; }

    /// <summary>
    /// UTC timestamp at which the action occurred.
    /// </summary>
    public DateTime TimestampUtc { get; init; }

    /// <summary>
    /// Originating IP address (IPv4 or IPv6) of the HTTP request.
    /// <see langword="null"/> for system-initiated actions with no HTTP context.
    /// </summary>
    public string? IpAddress { get; init; }

    /// <summary>
    /// JSON-serialised snapshot of the query parameters or request body associated
    /// with the action. <see langword="null"/> for actions that carry no queryable parameters.
    /// </summary>
    public string? Parameters { get; init; }

    /// <summary>
    /// Total number of records returned by a search or listing operation.
    /// <see langword="null"/> for action types that do not produce a result set.
    /// </summary>
    public int? ResultCount { get; init; }

    /// <summary>
    /// The S3 object key that was accessed during a single-record view operation.
    /// <see langword="null"/> for all other action types.
    /// </summary>
    public string? S3ObjectKey { get; init; }

    /// <summary>
    /// Snapshotted display name of the credential profile that was active at the
    /// time of the action. <see langword="null"/> for non-S3 actions.
    /// </summary>
    public string? ProfileName { get; init; }
}
