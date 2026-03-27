using System.ComponentModel.DataAnnotations;
using DataViewer.Domain.ValueObjects;

namespace DataViewer.Application.DTOs.Transactions;

/// <summary>
/// Query parameters for GET /api/transactions.
/// All filter fields are optional; pagination fields default to page 1 with
/// <see cref="SearchFilter.DefaultPageSize"/> records per page (API Design § 5.4).
/// </summary>
/// <remarks>
/// DataAnnotations validation is applied here so that ASP.NET Core model binding
/// rejects invalid requests before they reach the application layer.
/// The application layer then maps this DTO to a <see cref="SearchFilter"/> value
/// object for use-case execution.
/// </remarks>
public sealed record SearchTransactionsRequestDto
{
    /// <summary>
    /// Inclusive lower bound on the transaction's captured UTC timestamp, with explicit
    /// UTC offset. <see cref="DateTimeOffset"/> is used instead of <see cref="DateTime"/>
    /// to match the domain type (<c>SearchFilter.FromDate</c>) and ensure unambiguous
    /// timezone handling during filter evaluation.
    /// Only transactions with <c>TimestampUtc &gt;= FromDate</c> are returned.
    /// <see langword="null"/> means no lower time bound is applied.
    /// </summary>
    public DateTimeOffset? FromDate { get; init; }

    /// <summary>
    /// Inclusive upper bound on the transaction's captured UTC timestamp, with explicit
    /// UTC offset. <see cref="DateTimeOffset"/> is used instead of <see cref="DateTime"/>
    /// to match the domain type (<c>SearchFilter.ToDate</c>) and ensure unambiguous
    /// timezone handling during filter evaluation.
    /// Only transactions with <c>TimestampUtc &lt;= ToDate</c> are returned.
    /// <see langword="null"/> means no upper time bound is applied.
    /// </summary>
    public DateTimeOffset? ToDate { get; init; }

    /// <summary>
    /// Exact HTTP status code to filter by (e.g. <c>200</c>, <c>404</c>).
    /// When set, takes precedence over <see cref="StatusClass"/>.
    /// <see langword="null"/> means any status code is accepted.
    /// </summary>
    [Range(100, 599, ErrorMessage = "StatusCode must be a valid HTTP status code between 100 and 599.")]
    public int? StatusCode { get; init; }

    /// <summary>
    /// HTTP status-class prefix used to match a range of codes.
    /// Valid values: <c>"1"</c>, <c>"2"</c>, <c>"3"</c>, <c>"4"</c>, <c>"5"</c>.
    /// Ignored when <see cref="StatusCode"/> is also supplied.
    /// <see langword="null"/> means any status class is accepted.
    /// </summary>
    /// <remarks>
    /// <c>[RegularExpression("^[1-5]$")]</c> already enforces both the character range
    /// and the single-character length constraint, making a separate <c>[MaxLength(1)]</c>
    /// annotation entirely redundant — it has been intentionally omitted here.
    /// </remarks>
    [RegularExpression("^[1-5]$", ErrorMessage = "StatusClass must be a single digit between 1 and 5.")]
    public string? StatusClass { get; init; }

    /// <summary>
    /// HTTP method to filter by (e.g. <c>GET</c>, <c>POST</c>, <c>DELETE</c>).
    /// Comparison is case-insensitive. <see langword="null"/> = any method.
    /// </summary>
    [MaxLength(10, ErrorMessage = "Method must not exceed 10 characters.")]
    public string? Method { get; init; }

    /// <summary>
    /// URL path prefix used to scope results (e.g. <c>/api/orders</c>).
    /// Only transactions whose URL path starts with this value are returned.
    /// <see langword="null"/> = any URL path.
    /// </summary>
    [MaxLength(2048, ErrorMessage = "UrlPrefix must not exceed 2048 characters.")]
    public string? UrlPrefix { get; init; }

    /// <summary>
    /// 1-based page number. Must be at least 1.
    /// Defaults to 1 when not supplied.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Page must be at least 1.")]
    public int Page { get; init; } = 1;

    /// <summary>
    /// Number of records to return per page.
    /// Must be between 1 and <see cref="SearchFilter.MaxPageSize"/> (200).
    /// Defaults to <see cref="SearchFilter.DefaultPageSize"/> (50) when not supplied.
    /// </summary>
    [Range(1, SearchFilter.MaxPageSize, ErrorMessage = "PageSize must be between 1 and 200.")]
    public int PageSize { get; init; } = SearchFilter.DefaultPageSize;

    /// <summary>
    /// The ID of the credential profile to use for this search.
    /// When <see langword="null"/>, the currently active profile is used.
    /// </summary>
    public Guid? ProfileId { get; init; }
}
