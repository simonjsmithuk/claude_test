namespace DataViewer.Infrastructure.S3.MetadataExtractors;

using System.Text.Json.Serialization;

/// <summary>
/// Internal JSON model used to deserialise a transaction sidecar file
/// (<c>…_meta.json</c>) stored alongside the primary S3 transaction object.
/// </summary>
/// <remarks>
/// <para>
/// The capture agent is expected to write a JSON file with this schema next to
/// each primary transaction file. Every field is optional so that older sidecar
/// files (written before all fields were introduced) can still be partially parsed
/// without error.
/// </para>
/// <para>
/// All property names use <c>camelCase</c> to match the serialisation convention of
/// the capture agent. The <see cref="JsonPropertyName"/> attribute maps each C#
/// property to its wire name explicitly, making the class robust against future
/// C# property renames.
/// </para>
/// <para>
/// This type is intentionally <c>internal</c>; it is a raw deserialisation target
/// that is immediately mapped to the domain <see cref="DataViewer.Domain.ValueObjects.TransactionMetadata"/>
/// value object before leaving the extractor.
/// </para>
/// </remarks>
internal sealed class SidecarDocument
{
    /// <summary>
    /// HTTP method of the captured request (e.g. <c>"GET"</c>).
    /// </summary>
    [JsonPropertyName("method")]
    public string? Method { get; init; }

    /// <summary>
    /// HTTP status code of the captured response (e.g. <c>200</c>).
    /// </summary>
    [JsonPropertyName("statusCode")]
    public int? StatusCode { get; init; }

    /// <summary>
    /// URL path component of the captured request (e.g. <c>"/api/orders/42"</c>).
    /// </summary>
    [JsonPropertyName("urlPath")]
    public string? UrlPath { get; init; }

    /// <summary>
    /// UTC timestamp at which the HTTP transaction was captured.
    /// Expected in ISO 8601 format (e.g. <c>"2024-01-15T14:30:22Z"</c>).
    /// </summary>
    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset? TimestampUtc { get; init; }

    /// <summary>
    /// Size of the gzip-compressed primary transaction file in bytes.
    /// May be <see langword="null"/> when the capture agent did not record it.
    /// </summary>
    [JsonPropertyName("compressedSizeBytes")]
    public long? CompressedSizeBytes { get; init; }

    /// <summary>
    /// Size of the decompressed primary transaction content in bytes.
    /// May be <see langword="null"/> when decompression was not attempted at capture time.
    /// </summary>
    [JsonPropertyName("decompressedSizeBytes")]
    public long? DecompressedSizeBytes { get; init; }
}
