namespace DataViewer.Infrastructure.S3.MetadataExtractors;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Strongly-typed configuration options for the metadata extractor subsystem,
/// bound from the <c>S3</c> configuration section via
/// <see cref="Microsoft.Extensions.Options.IOptions{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Configuration key: <c>S3:MetadataExtractorStrategy</c>.
/// Valid values: <c>"path"</c> (default) | <c>"sidecar"</c>.
/// </para>
/// <para>
/// The DI composition root reads <see cref="MetadataExtractorStrategy"/> to select
/// which <see cref="DataViewer.Application.Interfaces.IMetadataExtractor"/>
/// implementation is registered as the active extractor.
/// </para>
/// <para>
/// Data-annotation validation is enforced at application startup via
/// <c>AddOptions&lt;MetadataExtractorOptions&gt;().Bind(...).ValidateDataAnnotations().ValidateOnStart()</c>
/// in
/// <see cref="DataViewer.Infrastructure.DependencyInjection.InfrastructureServiceExtensions"/>.
/// Misconfigured values therefore surface as an
/// <see cref="Microsoft.Extensions.Options.OptionsValidationException"/> during host
/// startup rather than at the first extractor resolution.
/// </para>
/// </remarks>
public sealed class MetadataExtractorOptions
{
    /// <summary>
    /// The configuration section name used to bind this options object.
    /// Shares the <c>S3</c> section with
    /// <see cref="DataViewer.Infrastructure.S3.Parsers.TransactionParserOptions"/>.
    /// </summary>
    public const string SectionName = "S3";

    /// <summary>
    /// Selects the active metadata extractor implementation.
    /// </summary>
    /// <value>
    /// <c>"path"</c> — uses <see cref="PathEncodedMetadataExtractor"/>, which parses
    /// transaction metadata from the convention-encoded S3 key path
    /// (e.g. <c>yyyy/MM/dd/{method}/{status}/{uuid}.bin</c>).<br/>
    /// <c>"sidecar"</c> — uses <see cref="SidecarMetadataExtractor"/> (stub;
    /// <see cref="SidecarMetadataExtractor.ExtractFromKey"/> is not yet implemented —
    /// see OQ-02).
    /// </value>
    [AllowedValues("path", "sidecar",
        ErrorMessage = "S3:MetadataExtractorStrategy must be 'path' or 'sidecar'.")]
    public string MetadataExtractorStrategy { get; init; } = "path";
}
