namespace DataViewer.Infrastructure.S3.Parsers;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Strongly-typed configuration options for the transaction parser subsystem,
/// bound from the <c>S3</c> configuration section via <see cref="Microsoft.Extensions.Options.IOptions{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Configuration key: <c>S3:TransactionFormat</c>.
/// Valid values: <c>"delimiter"</c> (default) | <c>"sidecar"</c>.
/// </para>
/// <para>
/// The DI composition root reads <see cref="TransactionFormat"/> to select which
/// <see cref="DataViewer.Application.Interfaces.ITransactionParser"/> implementation
/// is keyed as the active parser (see
/// <see cref="DataViewer.Infrastructure.DependencyInjection.InfrastructureServiceExtensions"/>).
/// </para>
/// <para>
/// Data annotation validation is enforced at application startup via
/// <c>AddOptions&lt;TransactionParserOptions&gt;().Bind(...).ValidateDataAnnotations().ValidateOnStart()</c>
/// in <see cref="DataViewer.Infrastructure.DependencyInjection.InfrastructureServiceExtensions"/>.
/// Misconfigured values (e.g. an unrecognised format string or a non-positive size limit)
/// therefore surface as an <see cref="Microsoft.Extensions.Options.OptionsValidationException"/>
/// during host startup rather than at the first parser resolution.
/// </para>
/// </remarks>
public sealed class TransactionParserOptions
{
    /// <summary>
    /// The configuration section name used to bind this options object.
    /// </summary>
    public const string SectionName = "S3";

    /// <summary>
    /// Selects the active transaction parser implementation.
    /// </summary>
    /// <value>
    /// <c>"delimiter"</c> — uses <see cref="DelimiterTransactionParser"/>, which
    /// splits raw file bytes on <c>--- REQUEST ---</c> and <c>--- RESPONSE ---</c>
    /// text markers.<br/>
    /// <c>"sidecar"</c> — uses <see cref="SidecarTransactionParser"/> (stub;
    /// not yet implemented — see OQ-01).
    /// </value>
    [AllowedValues("delimiter", "sidecar",
        ErrorMessage = "S3:TransactionFormat must be 'delimiter' or 'sidecar'.")]
    public string TransactionFormat { get; init; } = "delimiter";

    /// <summary>
    /// The maximum number of bytes that a single transaction file may decompress
    /// to before the parser raises a <see cref="DataViewer.Domain.Exceptions.TransactionParseException"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This cap defends against zip-bomb payloads: a tiny gzip file that decompresses
    /// to gigabytes, exhausting process memory. Even when the S3 bucket is trusted,
    /// the defence-in-depth principle applies — a compromised or misconfigured bucket
    /// could serve malicious files.
    /// </para>
    /// <para>
    /// The limit is enforced inside <see cref="DelimiterTransactionParser"/>'s
    /// decompression loop. When the decompressed byte count exceeds this value a
    /// <see cref="DataViewer.Domain.Exceptions.TransactionParseException"/> is thrown
    /// immediately and the rented <see cref="System.Buffers.ArrayPool{T}"/> buffer is
    /// returned before the exception propagates.
    /// </para>
    /// <para>
    /// Default: 52,428,800 bytes (50 MB). Adjust via <c>S3:MaxDecompressedSizeBytes</c>
    /// in appsettings.json for environments that legitimately capture very large payloads.
    /// </para>
    /// </remarks>
    [Range(1, int.MaxValue,
        ErrorMessage = "S3:MaxDecompressedSizeBytes must be a positive integer.")]
    public int MaxDecompressedSizeBytes { get; init; } = 52_428_800; // 50 MB
}
