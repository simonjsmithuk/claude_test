using System.Collections.Frozen;
using DataViewer.Domain.Enums;

namespace DataViewer.Application.DTOs.Transactions;

/// <summary>
/// Parsed HTTP message data (either a request or a response) within a
/// <see cref="TransactionDetailDto"/>. Both the request and the response sides
/// of a captured transaction carry the same four fields, so they share this
/// single DTO type (API Design § 5.4).
/// </summary>
/// <remarks>
/// <para>
/// Choosing a single shared type (<c>TransactionMessageDto</c>) rather than
/// separate <c>TransactionRequestDto</c> / <c>TransactionResponseDto</c> records
/// eliminates the structural duplication that existed in the original implementation,
/// where the two types were byte-for-byte identical. Consumers distinguish the
/// request from the response via their position in <see cref="TransactionDetailDto"/>
/// (<see cref="TransactionDetailDto.Request"/> vs <see cref="TransactionDetailDto.Response"/>).
/// </para>
/// <para>
/// <see cref="Headers"/> is typed as <see cref="IReadOnlyDictionary{TKey,TValue}"/>
/// rather than <see cref="Dictionary{TKey,TValue}"/> to prevent downstream code from
/// mutating the headers of an already-materialised read-model DTO. The default value
/// is <see cref="FrozenDictionary{TKey,TValue}.Empty"/>, which is a .NET 8 zero-allocation
/// singleton that also throws on any attempted mutation at runtime — providing
/// defence-in-depth beyond the interface type constraint alone.
/// </para>
/// </remarks>
public sealed record TransactionMessageDto
{
    /// <summary>
    /// HTTP headers, keyed by lower-case header name.
    /// Empty when the record contains no headers.
    /// </summary>
    /// <remarks>
    /// Typed as <see cref="IReadOnlyDictionary{TKey,TValue}"/> to signal immutability
    /// to callers. The default initialiser uses <see cref="FrozenDictionary{TKey,TValue}.Empty"/>
    /// — a .NET 8 zero-allocation singleton that throws on mutation — for defence-in-depth.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Headers { get; init; }
        = FrozenDictionary<string, string>.Empty;

    /// <summary>
    /// Decoded text of the HTTP body.
    /// <see langword="null"/> when the transaction record contains no body.
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// Detected content type of <see cref="Body"/>, used by the frontend to select
    /// an appropriate syntax-highlighting renderer.
    /// <see langword="null"/> when <see cref="Body"/> is <see langword="null"/>.
    /// </summary>
    public BodyContentType? BodyContentType { get; init; }

    /// <summary>
    /// <see langword="true"/> when the body stored in S3 was truncated at the
    /// capture-time size limit and therefore does not represent the full original payload.
    /// The frontend should surface a truncation warning to the user when this is true.
    /// </summary>
    public bool IsBodyTruncated { get; init; }
}
