namespace DataViewer.Application.Interfaces;

using DataViewer.Domain.ValueObjects;

/// <summary>
/// Extracts lightweight <see cref="TransactionMetadata"/> from an S3 object key
/// and/or an optional sidecar file, without downloading the full transaction body.
/// </summary>
/// <remarks>
/// By convention, S3 object keys for transaction records encode the HTTP method,
/// status code, URL path, and timestamp in their structure (e.g.
/// <c>transactions/2024/01/15/GET_200_api_orders_42_20240115T143022Z.gz</c>).
/// <see cref="ExtractFromKey"/> parses this convention to populate metadata cheaply
/// during listing operations.
///
/// <para>
/// Some S3 deployments also store a small JSON sidecar file alongside the main
/// transaction file (e.g. <c>…_meta.json</c>) containing pre-computed metadata
/// that may be richer than what the key alone can encode. <see cref="ExtractFromSidecar"/>
/// parses this sidecar when available, falling back to key-based extraction when the
/// sidecar is absent.
/// </para>
///
/// <para>
/// Both methods are synchronous because all work is pure string/byte parsing
/// with no I/O. The infrastructure implementation must not perform any S3 calls
/// within these methods.
/// </para>
/// </remarks>
public interface IMetadataExtractor
{
    /// <summary>
    /// Extracts transaction metadata by parsing the conventions encoded in the
    /// S3 object key alone.
    /// </summary>
    /// <param name="s3Key">
    /// The full S3 object key of the transaction file (e.g.
    /// <c>transactions/2024/01/15/GET_200_api_orders_42_20240115T143022Z.gz</c>).
    /// Must not be <see langword="null"/> or empty.
    /// </param>
    /// <returns>
    /// A <see cref="TransactionMetadata"/> populated from the key's encoded segments.
    /// Fields that cannot be derived from the key alone (e.g.
    /// <see cref="TransactionMetadata.DecompressedSizeBytes"/>) are set to
    /// their default sentinel values (<c>0</c>).
    /// </returns>
    /// <exception cref="DataViewer.Domain.Exceptions.DomainException">
    /// Thrown when the key does not conform to the expected naming convention and
    /// the required metadata fields cannot be extracted.
    /// </exception>
    TransactionMetadata ExtractFromKey(string s3Key);

    /// <summary>
    /// Extracts transaction metadata by parsing a JSON sidecar file, using the
    /// S3 object key to fill in fields absent from the sidecar.
    /// </summary>
    /// <param name="s3Key">
    /// The full S3 object key of the primary transaction file. Used as a fallback
    /// source for fields missing from the sidecar, and to populate
    /// <see cref="TransactionMetadata.S3Key"/> on the returned object.
    /// </param>
    /// <param name="sidecarBytes">
    /// The raw bytes of the JSON sidecar file (e.g. <c>…_meta.json</c>) downloaded
    /// from S3. Must not be <see langword="null"/> or empty.
    /// </param>
    /// <returns>
    /// A <see cref="TransactionMetadata"/> whose fields are populated preferentially
    /// from the sidecar JSON, with any missing fields falling back to key-based
    /// extraction via <see cref="ExtractFromKey"/>.
    /// </returns>
    /// <exception cref="DataViewer.Domain.Exceptions.DomainException">
    /// Thrown when neither the sidecar bytes nor the key can supply sufficient metadata
    /// to construct a valid <see cref="TransactionMetadata"/> record.
    /// </exception>
    TransactionMetadata ExtractFromSidecar(string s3Key, byte[] sidecarBytes);
}
