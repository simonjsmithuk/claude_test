namespace DataViewer.Domain.Exceptions;

/// <summary>
/// Thrown when a raw S3 transaction file cannot be parsed into a
/// <see cref="DataViewer.Domain.ValueObjects.ParsedTransaction"/> because the byte
/// content does not conform to any recognised transaction record format.
/// </summary>
/// <remarks>
/// <para>
/// This exception represents an <em>unrecoverable</em> format violation — one where
/// even a partial result cannot be meaningfully constructed. Examples include a file
/// whose required delimiter markers are entirely absent, or content that is not
/// valid gzip when gzip is mandatory.
/// </para>
/// <para>
/// Partial failures (e.g. a truncated body) are <strong>not</strong> represented by
/// this exception; instead they set the truncation flags on
/// <see cref="DataViewer.Domain.ValueObjects.ParsedTransaction.IsRequestBodyTruncated"/>
/// or <see cref="DataViewer.Domain.ValueObjects.ParsedTransaction.IsResponseBodyTruncated"/>.
/// </para>
/// <para>
/// Maps to <c>HTTP 422 Unprocessable Entity</c> in the API layer when the S3 object
/// was successfully downloaded but its content is malformed.
/// </para>
/// </remarks>
public sealed class TransactionParseException : DomainException
{
    /// <summary>
    /// Initialises the exception with a descriptive parse-failure message.
    /// </summary>
    /// <param name="message">
    /// Human-readable explanation of why the bytes could not be parsed. Should
    /// identify the specific format requirement that was violated (e.g. missing
    /// delimiter, unexpected encoding).
    /// </param>
    public TransactionParseException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises the exception with a descriptive parse-failure message and the
    /// lower-level exception that caused it.
    /// </summary>
    /// <param name="message">
    /// Human-readable explanation of why the bytes could not be parsed.
    /// </param>
    /// <param name="innerException">
    /// The lower-level exception (e.g. <see cref="System.IO.InvalidDataException"/>
    /// from GZipStream) that is the root cause of the parse failure.
    /// </param>
    public TransactionParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
