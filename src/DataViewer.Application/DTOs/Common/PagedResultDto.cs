namespace DataViewer.Application.DTOs.Common;

/// <summary>
/// Generic wrapper for paged API responses.
/// Returned by any endpoint that produces a paginated list (e.g. transaction search,
/// audit log listing).
/// </summary>
/// <typeparam name="T">
/// The type of the items in the paged result set (e.g.
/// <see cref="DataViewer.Application.DTOs.Transactions.TransactionSummaryDto"/>).
/// </typeparam>
public sealed record PagedResultDto<T>
{
    /// <summary>
    /// The items on the current page. Never <see langword="null"/>; an empty list
    /// is returned when no records match the query.
    /// </summary>
    /// <remarks>
    /// Typed as <see cref="IReadOnlyList{T}"/> rather than <see cref="List{T}"/> to
    /// prevent downstream callers from mutating the collection — consistent with the
    /// immutable read-model intent of all response DTOs.
    /// The C# 12 collection expression <c>[]</c> produces <c>Array.Empty&lt;T&gt;()</c>
    /// (a shared zero-allocation singleton), making the default initialiser efficient.
    /// </remarks>
    public IReadOnlyList<T> Data { get; init; } = [];

    /// <summary>
    /// Total number of records matching the query criteria across ALL pages.
    /// Used by clients to compute the total number of pages:
    /// <c>Math.Ceiling((double)TotalCount / PageSize)</c>.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// The 1-based page number of the data in <see cref="Data"/>.
    /// Matches the <c>page</c> query parameter supplied in the request.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// The maximum number of items per page used for this response.
    /// Matches the <c>pageSize</c> query parameter supplied in the request
    /// (after clamping to the server-enforced maximum).
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Convenience factory method to construct a <see cref="PagedResultDto{T}"/>
    /// from an already-paged data set.
    /// </summary>
    /// <param name="data">The items on the current page.</param>
    /// <param name="totalCount">Total matching records across all pages.</param>
    /// <param name="page">Current 1-based page number.</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <returns>A fully-populated <see cref="PagedResultDto{T}"/>.</returns>
    public static PagedResultDto<T> Create(
        IReadOnlyList<T> data,
        int totalCount,
        int page,
        int pageSize) =>
        new()
        {
            Data = data,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
}
