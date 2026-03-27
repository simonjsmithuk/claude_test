using DataViewer.Domain.Enums;

namespace DataViewer.Application.DTOs.AuditLogs;

/// <summary>
/// Encapsulates all filter and pagination parameters for the audit log read endpoint
/// (GET /api/admin/audit-logs).
/// </summary>
/// <remarks>
/// All filter fields are optional; when left <see langword="null"/> the corresponding
/// predicate is not applied, so the query degrades gracefully to an unfiltered,
/// time-ordered list.  Combining multiple filter fields produces a logical AND
/// (all conditions must be satisfied simultaneously).
///
/// <para>
/// <strong>Pagination:</strong> Pages are 1-based.  <see cref="Page"/> must be ≥ 1;
/// <see cref="PageSize"/> is clamped to [1, <see cref="MaxPageSize"/>] by the
/// repository to prevent runaway queries.
/// </para>
/// </remarks>
public sealed record AuditLogQueryParameters
{
    /// <summary>Maximum page size enforced server-side to cap result set cardinality.</summary>
    public const int MaxPageSize = 200;

    /// <summary>
    /// Default page size used when the caller does not supply an explicit value.
    /// </summary>
    public const int DefaultPageSize = 50;

    // ── Filter fields ────────────────────────────────────────────────────────

    /// <summary>
    /// When set, only entries belonging to this user are returned.
    /// Pass <see langword="null"/> to retrieve entries across all users.
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// When set, only entries whose <c>ActionType</c> matches this value are returned.
    /// Pass <see langword="null"/> to include all action types.
    /// </summary>
    public AuditActionType? ActionType { get; init; }

    /// <summary>
    /// Inclusive lower bound of the <c>TimestampUtc</c> range filter (UTC).
    /// When set, only entries with <c>TimestampUtc ≥ From</c> are included.
    /// Pass <see langword="null"/> for no lower bound.
    /// </summary>
    public DateTime? From { get; init; }

    /// <summary>
    /// Inclusive upper bound of the <c>TimestampUtc</c> range filter (UTC).
    /// When set, only entries with <c>TimestampUtc ≤ To</c> are included.
    /// Pass <see langword="null"/> for no upper bound.
    /// </summary>
    public DateTime? To { get; init; }

    // ── Pagination fields ────────────────────────────────────────────────────

    /// <summary>
    /// 1-based page number requested.  Defaults to 1.
    /// </summary>
    /// <remarks>
    /// A value less than 1 is treated as 1 by the repository to avoid negative
    /// OFFSET values in the generated SQL.
    /// </remarks>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Number of items per page.  Defaults to <see cref="DefaultPageSize"/>.
    /// The repository clamps this to [1, <see cref="MaxPageSize"/>] server-side.
    /// </summary>
    public int PageSize { get; init; } = DefaultPageSize;
}
