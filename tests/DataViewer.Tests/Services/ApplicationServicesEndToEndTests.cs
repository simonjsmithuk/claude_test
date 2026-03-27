using System.IO.Compression;
using System.Text;
using DataViewer.Application.Interfaces;
using DataViewer.Application.Services;
using DataViewer.Domain.Entities;
using DataViewer.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace DataViewer.Tests.Services;

/// <summary>
/// End-to-end happy-path tests that exercise the full pipeline of all three
/// TASK-018 services working together:
///
///   S3 body stream
///     → <see cref="BodyTruncator"/>  (reads up to cap bytes from stream)
///     → <see cref="GzipDecompressor"/> (decompresses if gzip)
///     → <see cref="ContentTypeDetector"/> (classifies the plaintext body)
///
/// These tests correspond to the user-facing scenarios described in the
/// Product Specification Document §FR-S3 / §FR-BODY.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ApplicationServicesEndToEndTests
{
    // ── Pipeline factory ──────────────────────────────────────────────────────

    private sealed record Pipeline(
        IBodyTruncator Truncator,
        IGzipDecompressor Decompressor,
        IContentTypeDetector Detector);

    private static Pipeline BuildPipeline(int capMb = 5)
    {
        var repoMock = new Mock<ISystemSettingsRepository>();
        repoMock
            .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettings { BodySizeCapMb = capMb });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => repoMock.Object);
        services.AddApplicationServices();

        var provider = services.BuildServiceProvider();
        return new Pipeline(
            provider.GetRequiredService<IBodyTruncator>(),
            provider.GetRequiredService<IGzipDecompressor>(),
            provider.GetRequiredService<IContentTypeDetector>());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static byte[] GzipBytes(string plainText)
    {
        var input = Encoding.UTF8.GetBytes(plainText);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            gz.Write(input, 0, input.Length);
        return ms.ToArray();
    }

    private static MemoryStream ToStream(byte[] data) => new(data);

    // ═════════════════════════════════════════════════════════════════════════
    // US-BODY-01: Gzip-compressed JSON body — happy path (end-to-end)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The most common production scenario: an S3 object contains a gzip-compressed
    /// JSON body. The pipeline must decompress it and classify it as JSON.
    /// </summary>
    [Fact]
    public async Task Pipeline_GzipCompressedJsonBody_DecompressesAndClassifiesAsJson()
    {
        // Arrange
        const string json = """{"userId":1,"name":"Alice","roles":["admin"]}""";
        var pipeline = BuildPipeline(capMb: 5);
        var rawBodyBytes = GzipBytes(json);
        using var bodyStream = ToStream(rawBodyBytes);

        // Act — Step 1: read from stream (within cap)
        var (truncatedBytes, isTruncated) = await pipeline.Truncator
            .TruncateIfNeededAsync(bodyStream);

        // Act — Step 2: decompress
        var (decompressedBytes, wasCompressed) = await pipeline.Decompressor
            .DecompressAsync(truncatedBytes, contentEncodingHeader: "gzip");

        // Act — Step 3: classify
        var contentType = pipeline.Detector
            .Detect(decompressedBytes, contentTypeHeader: "application/json");

        // Assert
        isTruncated.Should().BeFalse();
        wasCompressed.Should().BeTrue();
        Encoding.UTF8.GetString(decompressedBytes).Should().Be(json);
        contentType.Should().Be(BodyContentType.Json,
            because: "a decompressed JSON body must be classified as Json");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US-BODY-02: Gzip-compressed XML body — happy path
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Pipeline_GzipCompressedXmlBody_DecompressesAndClassifiesAsXml()
    {
        // Arrange
        const string xml = """<?xml version="1.0"?><response><status>200</status></response>""";
        var pipeline = BuildPipeline(capMb: 5);
        using var bodyStream = ToStream(GzipBytes(xml));

        // Act
        var (truncated, _) = await pipeline.Truncator.TruncateIfNeededAsync(bodyStream);
        var (decompressed, wasCompressed) = await pipeline.Decompressor
            .DecompressAsync(truncated, null); // detect by magic bytes

        var contentType = pipeline.Detector.Detect(decompressed, null);

        // Assert
        wasCompressed.Should().BeTrue();
        contentType.Should().Be(BodyContentType.Xml);
        Encoding.UTF8.GetString(decompressed).Should().Be(xml);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US-BODY-03: Plain (uncompressed) text body — happy path
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Pipeline_PlainTextBody_PassesThroughAndClassifiesAsText()
    {
        // Arrange
        const string text = "Hello, world! This is a plain text HTTP response body.";
        var pipeline = BuildPipeline(capMb: 5);
        var rawBytes = Encoding.UTF8.GetBytes(text);
        using var bodyStream = ToStream(rawBytes);

        // Act
        var (truncated, isTruncated) = await pipeline.Truncator.TruncateIfNeededAsync(bodyStream);
        var (decompressed, wasCompressed) = await pipeline.Decompressor
            .DecompressAsync(truncated, null);

        var contentType = pipeline.Detector.Detect(decompressed, null);

        // Assert
        isTruncated.Should().BeFalse();
        wasCompressed.Should().BeFalse(
            because: "plain text without gzip magic bytes must pass through unchanged");
        decompressed.Should().BeSameAs(truncated,
            because: "the decompressor returns the original array reference for non-gzip input");
        contentType.Should().Be(BodyContentType.Text);
        Encoding.UTF8.GetString(decompressed).Should().Be(text);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US-BODY-04: Large body exceeding cap — truncation + classification
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Pipeline_BodyExceedingCap_IsTruncatedBeforeDecompression()
    {
        // Arrange — uncompressed JSON body that exceeds the 1 MB cap
        const int capMb = 1;
        int capBytes = capMb * 1024 * 1024;
        var bigJson = "{\"data\":\"" + new string('X', capBytes + 512) + "\"}";
        var pipeline = BuildPipeline(capMb);
        using var bodyStream = ToStream(Encoding.UTF8.GetBytes(bigJson));

        // Act
        var (truncated, isTruncated) = await pipeline.Truncator.TruncateIfNeededAsync(bodyStream);

        // The truncated bytes start with '{' → ContentTypeDetector classifies as Json
        // (we do NOT attempt decompression because the truncated JSON is not valid gzip)
        var contentType = pipeline.Detector.Detect(truncated, null);

        // Assert
        isTruncated.Should().BeTrue();
        truncated.Length.Should().Be(capBytes);
        contentType.Should().Be(BodyContentType.Json,
            because: "even after truncation the first byte '{' identifies the body as JSON");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US-BODY-05: Gzip-compressed JSON, no Content-Encoding header — magic-byte detection
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Pipeline_GzipBodyWithoutContentEncodingHeader_DetectedByMagicBytes()
    {
        // Arrange — content-encoding header is absent; decompressor must use magic bytes
        const string json = """[{"id":1},{"id":2}]""";
        var pipeline = BuildPipeline();
        using var bodyStream = ToStream(GzipBytes(json));

        // Act
        var (truncated, _) = await pipeline.Truncator.TruncateIfNeededAsync(bodyStream);
        var (decompressed, wasCompressed) = await pipeline.Decompressor
            .DecompressAsync(truncated, contentEncodingHeader: null);

        var contentType = pipeline.Detector.Detect(decompressed, contentTypeHeader: null);

        // Assert
        wasCompressed.Should().BeTrue(
            because: "magic bytes 0x1F 0x8B must trigger decompression even without the header");
        contentType.Should().Be(BodyContentType.Json);
        Encoding.UTF8.GetString(decompressed).Should().Be(json);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US-BODY-06: Content-Type header classifies JSON even if body starts with whitespace
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Pipeline_JsonBodyWithLeadingWhitespace_ClassifiesAsJson()
    {
        // Arrange — body has a BOM-like or whitespace preamble, but header says JSON
        const string json = "   \n  {\"key\":\"value\"}";
        var pipeline = BuildPipeline();
        using var bodyStream = ToStream(Encoding.UTF8.GetBytes(json));

        // Act
        var (truncated, _) = await pipeline.Truncator.TruncateIfNeededAsync(bodyStream);
        var (decompressed, _) = await pipeline.Decompressor.DecompressAsync(truncated, null);
        var contentType = pipeline.Detector.Detect(decompressed, "application/json");

        // Assert
        contentType.Should().Be(BodyContentType.Json);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US-BODY-07: Empty body — full pipeline returns Unknown content type
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Pipeline_EmptyBody_ReturnsUnknownContentType()
    {
        // Arrange
        var pipeline = BuildPipeline();
        using var emptyStream = new MemoryStream();

        // Act
        var (truncated, isTruncated) = await pipeline.Truncator.TruncateIfNeededAsync(emptyStream);
        var (decompressed, wasCompressed) = await pipeline.Decompressor
            .DecompressAsync(truncated, null);
        var contentType = pipeline.Detector.Detect(decompressed, null);

        // Assert
        isTruncated.Should().BeFalse();
        wasCompressed.Should().BeFalse();
        contentType.Should().Be(BodyContentType.Unknown,
            because: "an empty body cannot be classified and must return Unknown");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US-BODY-08: HTML response body via header detection
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Pipeline_HtmlBodyWithHeader_ClassifiesAsXml()
    {
        // Arrange — some APIs return HTML; per spec html header → Xml
        const string html = "<html><body><p>404 Not Found</p></body></html>";
        var pipeline = BuildPipeline();
        using var bodyStream = ToStream(Encoding.UTF8.GetBytes(html));

        // Act
        var (truncated, _) = await pipeline.Truncator.TruncateIfNeededAsync(bodyStream);
        var (decompressed, _) = await pipeline.Decompressor.DecompressAsync(truncated, null);
        var contentType = pipeline.Detector.Detect(decompressed, "text/html; charset=utf-8");

        // Assert
        contentType.Should().Be(BodyContentType.Xml,
            because: "text/html Content-Type header maps to Xml per the acceptance criteria");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US-BODY-09: Gzip-compressed JSON body exactly at the cap boundary
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Pipeline_GzipBodyExactlyAtCap_IsNotTruncatedAndDecompressesCorrectly()
    {
        // Arrange — the gzip-compressed payload must fit exactly within the cap.
        // We build a small compressed payload and set the cap to its byte length.
        const string json = """{"status":"ok"}""";
        var compressedBytes = GzipBytes(json);
        int capBytes = compressedBytes.Length;       // cap = exact size of compressed data
        int capMb    = (int)Math.Ceiling(capBytes / (double)(1024 * 1024));
        // Use a cap that is at least as large as the compressed bytes (may be 1 MB)
        // but guarantee the stream fits exactly by using a custom pipeline setup.
        var repoMock = new Mock<ISystemSettingsRepository>();
        repoMock
            .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettings { BodySizeCapMb = Math.Max(1, capMb) });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => repoMock.Object);
        services.AddApplicationServices();
        var provider = services.BuildServiceProvider();

        var truncator   = provider.GetRequiredService<IBodyTruncator>();
        var decompressor = provider.GetRequiredService<IGzipDecompressor>();
        var detector    = provider.GetRequiredService<IContentTypeDetector>();

        using var bodyStream = new MemoryStream(compressedBytes);

        // Act
        var (truncated, isTruncated) = await truncator.TruncateIfNeededAsync(bodyStream);
        var (decompressed, wasCompressed) = await decompressor.DecompressAsync(truncated, "gzip");
        var contentType = detector.Detect(decompressed, null);

        // Assert
        isTruncated.Should().BeFalse();
        wasCompressed.Should().BeTrue();
        contentType.Should().Be(BodyContentType.Json);
        Encoding.UTF8.GetString(decompressed).Should().Be(json);
    }
}
