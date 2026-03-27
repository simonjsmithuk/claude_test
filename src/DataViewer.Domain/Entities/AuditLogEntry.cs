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
/// Use the <see cref="CreateForUser"/> and <see cref="CreateForSystem"/> factory methods
/// rather than direct construction to ensure <see cref="UserId"/> is never accidentally
/// left as <see cref="Guid.Empty"/>, which would silently produce an unattributable
/// audit record.
/// </para>
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
    // Private constructor — callers must use the factory methods to ensure
    // UserId is validated at construction time.
    private AuditLogEntry() { }

    /// <summary>
    /// Primary key. Generated once on entity construction; never reassigned.
    /// </summary>
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>
    /// The <see cref="User.Id"/> of the user who triggered the action.
    /// Stored as a value (not a navigation property) to ensure log entries survive
    /// user account deletion or deactivation without orphaned foreign keys.
    /// By design — no navigation property. See ADR-004.
    /// </summary>
    /// <remarks>
    /// For system-initiated actions with no authenticated user (e.g. background-job
    /// account lockouts) use <see cref="CreateForSystem"/>; the value is set to
    /// <see cref="Guid.Empty"/> in that case and must be interpreted accordingly
    /// in audit log viewers.
    /// </remarks>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Categorical type of the operation that was performed.
    /// The underlying integer value is persisted to the database;
    /// <see cref="AuditActionType"/> members must not be reordered or renumbered.
    /// </summary>
    public AuditActionType ActionType { get; private set; }

    /// <summary>
    /// UTC timestamp at which the action occurred, captured server-side before the
    /// response is dispatched to the client.
    /// </summary>
    public DateTime TimestampUtc { get; private set; }

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
    public string? IpAddress { get; private set; }

    /// <summary>
    /// JSON-serialised snapshot of the query parameters or request body associated
    /// with the action (e.g. search filter criteria for
    /// <see cref="AuditActionType.SearchTransactions"/>).
    /// <see langword="null"/> for actions that carry no queryable parameters
    /// (e.g. <see cref="AuditActionType.Login"/>, <see cref="AuditActionType.Logout"/>).
    /// </summary>
    /// <remarks>
    /// ⚠️ Security: The Infrastructure serialiser MUST produce well-formed JSON and
    /// MUST NOT interpolate raw user-supplied strings directly into this field.
    /// Unescaped input could embed JSON that falsifies the apparent content of other
    /// fields when the log is rendered in a UI that auto-parses the JSON payload
    /// (log injection). Always use a proper JSON serialiser (e.g. System.Text.Json)
    /// to produce this value.
    /// </remarks>
    public string? Parameters { get; private set; }

    /// <summary>
    /// Total number of records returned by a search or listing operation.
    /// <see langword="null"/> for action types that do not produce a result set
    /// (e.g. <see cref="AuditActionType.ViewTransaction"/>, <see cref="AuditActionType.Login"/>).
    /// </summary>
    public int? ResultCount { get; private set; }

    /// <summary>
    /// The S3 object key of the record that was accessed during a single-record
    /// view operation (<see cref="AuditActionType.ViewTransaction"/>).
    /// <see langword="null"/> for all other action types.
    /// </summary>
    public string? S3ObjectKey { get; private set; }

    /// <summary>
    /// Display name of the <see cref="CredentialProfile"/> that was active at the
    /// time of the action, snapshotted here so that the log entry remains meaningful
    /// even after the profile is renamed, deactivated, or soft-deleted.
    /// <see langword="null"/> for non-S3 actions (e.g. <see cref="AuditActionType.Login"/>,
    /// <see cref="AuditActionType.Logout"/>).
    /// </summary>
    public string? ProfileName { get; private set; }

    // ── Factory methods ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates an audit log entry attributed to a known, authenticated user.
    /// </summary>
    /// <param name="userId">
    /// The <see cref="User.Id"/> of the user performing the action.
    /// Must not be <see cref="Guid.Empty"/>; pass <see cref="CreateForSystem"/>
    /// for system-initiated actions.
    /// </param>
    /// <param name="actionType">The category of action being recorded.</param>
    /// <param name="timestampUtc">The UTC instant at which the action occurred.</param>
    /// <param name="ipAddress">Originating IP address, or <see langword="null"/> if unavailable.</param>
    /// <param name="parameters">JSON-serialised parameter snapshot, or <see langword="null"/>.</param>
    /// <param name="resultCount">Result count for search/list operations, or <see langword="null"/>.</param>
    /// <param name="s3ObjectKey">S3 object key for view operations, or <see langword="null"/>.</param>
    /// <param name="profileName">Active credential profile name snapshot, or <see langword="null"/>.</param>
    /// <returns>A fully-populated, valid <see cref="AuditLogEntry"/>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="userId"/> is <see cref="Guid.Empty"/>.
    /// </exception>
    public static AuditLogEntry CreateForUser(
        Guid userId,
        AuditActionType actionType,
        DateTime timestampUtc,
        string? ipAddress = null,
        string? parameters = null,
        int? resultCount = null,
        string? s3ObjectKey = null,
        string? profileName = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException(
                "UserId must not be Guid.Empty. Use CreateForSystem() for system-initiated audit entries.",
                nameof(userId));

        return new AuditLogEntry
        {
            UserId = userId,
            ActionType = actionType,
            TimestampUtc = timestampUtc,
            IpAddress = ipAddress,
            Parameters = parameters,
            ResultCount = resultCount,
            S3ObjectKey = s3ObjectKey,
            ProfileName = profileName
        };
    }

    /// <summary>
    /// Creates an audit log entry for a system-initiated action that has no
    /// authenticated user context (e.g. a background job that locks an account
    /// after a threshold is exceeded).
    /// </summary>
    /// <param name="actionType">The category of action being recorded.</param>
    /// <param name="timestampUtc">The UTC instant at which the action occurred.</param>
    /// <param name="parameters">JSON-serialised parameter snapshot, or <see langword="null"/>.</param>
    /// <returns>
    /// A valid <see cref="AuditLogEntry"/> with <see cref="UserId"/> set to
    /// <see cref="Guid.Empty"/> to explicitly signal a system-originated action.
    /// Audit log viewers must render this sentinel value as "System" rather than
    /// as an unresolvable user ID.
    /// </returns>
    public static AuditLogEntry CreateForSystem(
        AuditActionType actionType,
        DateTime timestampUtc,
        string? parameters = null)
    {
        // ASSUMPTION: Guid.Empty is the agreed sentinel for system-initiated actions.
        // Audit log UI must handle this value explicitly and display it as "System".
        return new AuditLogEntry
        {
            UserId = Guid.Empty,
            ActionType = actionType,
            TimestampUtc = timestampUtc,
            Parameters = parameters
        };
    }
}
