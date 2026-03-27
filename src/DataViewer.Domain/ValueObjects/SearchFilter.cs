namespace DataViewer.Domain.ValueObjects;

/// <summary>
/// Immutable value object that encapsulates the criteria supplied by a user when
/// searching for transaction records.
/// </summary>
/// <remarks>
/// <para>
/// All filter properties are optional except <see cref="Page"/> and <see cref="PageSize"/>,
/// which default to page 1 with a page size of 50. When a filter property is
/// <see langword="null"/>, that dimension is unconstrained.
/// </para>
/// <para>
/// <see cref="PageSize"/> is capped at <see cref="MaxPageSize"/> (200) to prevent
/// unbounded result sets. Values supplied above the cap are silently clamped at
/// construction time — callers do not need to validate this themselves.
/// </para>
/// <para>
/// <see cref="StatusClass"/> is a coarse filter expressed as the HTTP status-class
/// prefix string (e.g. <c>"2"</c> matches all 2xx codes, <c>"4"</c> matches all 4xx).
/// It is mutually exclusive with <see cref="StatusCode"/> at the application layer;
/// if both are provided, <see cref="StatusCode"/> takes precedence.
/// </para>
/// <para>
/// <see cref="UrlPrefix"/> supports prefix-matching on the URL path stored in S3
/// object keys (e.g. <c>"/api/orders"</c> matches <c>"/api/orders/1"</c> and
/// <c>"/api/orders/2"</c>).
/// </para>
/// <para>
/// Date-range bounds are <see cref="DateTimeOffset"/> to carry unambiguous UTC context.
/// Callers must ensure both <see cref="FromDate"/> and <see cref="ToDate"/> use the
/// same UTC offset (ideally <c>+00:00</c>) to prevent silent timezone comparison errors.
/// </para>
/// </remarks>
public record SearchFilter
{
    // ── Public constants ─────────────────────────────────────────────────────

    /// <summary>Maximum allowed page size. Requests above this value are clamped.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Default page size used when the caller does not supply one.</summary>
    public const int DefaultPageSize = 50;

    // ── Constructor ──────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a <see cref="SearchFilter"/> with the supplied criteria,
    /// enforcing the <see cref="MaxPageSize"/> cap and minimum-value guards on
    /// <see cref="Page"/> and <see cref="PageSize"/>.
    /// </summary>
    /// <param name="fromDate">Inclusive lower bound on <c>TimestampUtc</c>. <see langword="null"/> = no lower bound.</param>
    /// <param name="toDate">Inclusive upper bound on <c>TimestampUtc</c>. <see langword="null"/> = no upper bound.</param>
    /// <param name="statusCode">Exact HTTP status code to match. <see langword="null"/> = any status code.</param>
    /// <param name="statusClass">HTTP status class prefix to match (e.g. <c>"2"</c> for 2xx). <see langword="null"/> = any class.</param>
    /// <param name="method">HTTP method to match (e.g. <c>"GET"</c>). <see langword="null"/> = any method.</param>
    /// <param name="urlPrefix">URL path prefix to match (e.g. <c>"/api/"</c>). <see langword="null"/> = any path.</param>
    /// <param name="page">1-based page number. Values less than 1 are clamped to 1.</param>
    /// <param name="pageSize">Number of records per page. Values less than 1 are clamped to 1; values above <see cref="MaxPageSize"/> are clamped to <see cref="MaxPageSize"/>.</param>
    public SearchFilter(
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        int? statusCode = null,
        string? statusClass = null,
        string? method = null,
        string? urlPrefix = null,
        int page = 1,
        int pageSize = DefaultPageSize)
    {
        FromDate = fromDate;
        ToDate = toDate;
        StatusCode = statusCode;
        StatusClass = statusClass;
        Method = method;
        UrlPrefix = urlPrefix;

        // Guard: page must be at least 1
        Page = page < 1 ? 1 : page;

        // Guard: page size must be between 1 and MaxPageSize (inclusive)
        PageSize = pageSize < 1 ? 1
                 : pageSize > MaxPageSize ? MaxPageSize
                 : pageSize;
    }

    // ── Properties ───────────────────────────────────────────────────────────

    /// <summary>
    /// Inclusive lower bound on the transaction's captured UTC timestamp.
    /// Only transactions with <c>TimestampUtc &gt;= FromDate</c> are returned.
    /// <see langword="null"/> means no lower time bound is applied.
    /// </summary>
    public DateTimeOffset? FromDate { get; init; }

    /// <summary>
    /// Inclusive upper bound on the transaction's captured UTC timestamp.
    /// Only transactions with <c>TimestampUtc &lt;= ToDate</c> are returned.
    /// <see langword="null"/> means no upper time bound is applied.
    /// </summary>
    public DateTimeOffset? ToDate { get; init; }

    /// <summary>
    /// Exact HTTP status code to filter by (e.g. <c>200</c>, <c>404</c>).
    /// When set, takes precedence over <see cref="StatusClass"/>.
    /// <see langword="null"/> means any status code is accepted.
    /// </summary>
    public int? StatusCode { get; init; }

    /// <summary>
    /// HTTP status-class prefix used to match a range of codes (e.g. <c>"2"</c> for
    /// all 2xx responses, <c>"5"</c> for all 5xx responses).
    /// Ignored when <see cref="StatusCode"/> is non-null.
    /// <see langword="null"/> means any status class is accepted.
    /// </summary>
    public string? StatusClass { get; init; }

    /// <summary>
    /// HTTP method to filter by (e.g. <c>"GET"</c>, <c>"POST"</c>).
    /// Comparison is case-insensitive at the application layer.
    /// <see langword="null"/> means any HTTP method is accepted.
    /// </summary>
    public string? Method { get; init; }

    /// <summary>
    /// URL path prefix used to scope results (e.g. <c>"/api/orders"</c>).
    /// Only transactions whose URL path starts with this value are returned.
    /// <see langword="null"/> means any URL path is accepted.
    /// </summary>
    public string? UrlPrefix { get; init; }

    /// <summary>
    /// 1-based page number. Always &gt;= 1 after construction.
    /// Used together with <see cref="PageSize"/> to compute the S3 listing offset.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Number of records to return per page. Always in the range
    /// <c>[1, <see cref="MaxPageSize"/>]</c> after construction.
    /// </summary>
    public int PageSize { get; init; }
}
