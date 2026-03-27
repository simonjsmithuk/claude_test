#nullable enable

namespace DataViewer.Domain.Entities;

using DataViewer.Domain.Enums;

/// <summary>
/// An immutable record of a single auditable action performed within DataViewer.
/// </summary>
/// <remarks>
/// Audit log entries are append-only: they are never updated or hard-deleted through
/// application APIs. This design guarantees a complete, tamper-evident history of all
/// data-access and administration operations (Product Spec § G-03).
///
/// <para>
/// The <see cref="Parameters"/> field stores a compact JSON snapshot of the search
/// filters or request context active at the time of the action so that each log
/// entry is self-contained and human-readable without joining other tables.
/// </para>
///
/// <para>
/// Optional fields (<see cref="ResultCount"/>, <see cref="S3ObjectKey"/>,
/// <see cref="ProfileName"/>) are only populated when semantically relevant to the
/// <see cref="ActionType"/>; all others are left <see langword="null"/>.
/// </para>
///
/// <para>
/// <see cref="UserId"/> is stored as a plain value rather than a navigation property
/// so that log entries remain fully intact even if the referenced user account is
/// subsequently deactivated or removed from the system.
/// By design — no navigation property. See ADR-004.
/// </para>
/// </remarks>
public class AuditLogEntry
{
    /// <summary>
    /// Primary key. Generated once on entity construction; never reassigned.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The <see cref="User.Id"/> of the user who triggered the action.
    /// Stored as a value (not a navigation property) to ensure log entries survive
    /// user account deletion or deactivation without orphaned foreign keys.
    /// By design — no navigation property. See ADR-004.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Categorical type of the operation that was performed.
    /// The underlying integer value is persisted to the database;
    /// <see cref="AuditActionType"/> members must not be reordered or renumbered.
    /// </summary>
    public AuditActionType ActionType { get; set; }

    /// <summary>
    /// UTC timestamp at which the action occurred, captured server-side before the
    /// response is dispatched to the client.
    /// Initialised to <see langword="default"/> here; the application layer MUST
    /// assign this value explicitly (e.g. <c>entry.TimestampUtc = DateTime.UtcNow</c>)
    /// before persisting the entry so that it reflects the actual action time rather
    /// than the object construction time. Capturing server-side eliminates skew from
    /// client clocks and guarantees every operation is recorded regardless of client
    /// connectivity issues.
    /// </summary>
    public DateTime TimestampUtc { get; set; } = default;

    /// <summary>
    /// Originating IP address (IPv4 or IPv6) of the HTTP request.
    /// <see langword="null"/> for system-initiated actions that have no HTTP context
    /// (e.g. background jobs that write <see cref="AuditActionType.AccountLocked"/>
    /// entries). An empty string must not be used as a sentinel — use <see langword="null"/>
    /// when no IP address is available.
    /// </summary>
    /// <remarks>
    /// When extracted from the <c>X-Forwarded-For</c> header, the Infrastructure
    /// layer MUST validate that the header originates from a known trusted proxy CIDR
    /// before using its value. Blindly trusting <c>X-Forwarded-For</c> is an IP-spoofing
    /// vector. Only the first non-private address in the forwarded chain should be used
    /// after the proxy whitelist check passes.
    /// </remarks>
    public string? IpAddress { get; set; }

    /// <summary>
    /// JSON-serialised snapshot of the query parameters or request body associated
    /// with the action (e.g. search filter criteria for
    /// <see cref="AuditActionType.SearchTransactions"/>).
    /// <see langword="null"/> for actions that carry no queryable parameters
    /// (e.g. <see cref="AuditActionType.Login"/>, <see cref="AuditActionType.Logout"/>).
    /// </summary>
    public string? Parameters { get; set; }

    /// <summary>
    /// Total number of records returned by a search or listing operation.
    /// <see langword="null"/> for action types that do not produce a result set
    /// (e.g. <see cref="AuditActionType.ViewTransaction"/>, <see cref="AuditActionType.Login"/>).
    /// </summary>
    public int? ResultCount { get; set; }

    /// <summary>
    /// The S3 object key of the record that was accessed during a single-record
    /// view operation (<see cref="AuditActionType.ViewTransaction"/>).
    /// <see langword="null"/> for all other action types.
    /// </summary>
    public string? S3ObjectKey { get; set; }

    /// <summary>
    /// Display name of the <see cref="CredentialProfile"/> that was active at the
    /// time of the action, snapshotted here so that the log entry remains meaningful
    /// even after the profile is renamed, deactivated, or soft-deleted.
    /// <see langword="null"/> for non-S3 actions (e.g. <see cref="AuditActionType.Login"/>,
    /// <see cref="AuditActionType.Logout"/>).
    /// </summary>
    public string? ProfileName { get; set; }
}
