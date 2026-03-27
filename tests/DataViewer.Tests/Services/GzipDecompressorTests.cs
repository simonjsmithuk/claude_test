using System.IO.Compression;
using System.Text;
using DataViewer.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataViewer.Tests.Services;

/// <summary>
/// Unit tests for <see cref="GzipDecompressor"/>.
/// Covers magic-byte detection, Content-Encoding header detection, streaming
/// decompression, pass-through behaviour, and all edge / error conditions.
/// </summary>
[Trait("Category", "Unit")]
public sealed class GzipDecompressorTests
{
    // ── System-under-test factory ─────────────────────────────────────────────

    private static GzipDecompressor CreateSut() =>
        new(NullLogger<GzipDecompressor>.Instance);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Compresses <paramref name="plainText"/> using <see cref="GZipStream"/> and
    /// returns the resulting gzip byte array (starts with magic bytes 0x1F 0x8B).
    /// </summary>
    private static byte[] Gzip(string plainText)
    {
        var inputBytes = Encoding.UTF8.GetBytes(plainText);
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gz.Write(inputBytes, 0, inputBytes.Length);
        }
        return output.ToArray();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Constructor guard
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new GzipDecompressor(null!);

        // Assert
        act.Should().ThrowExactly<ArgumentNullException>()
           .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WhenLoggerIsValid_DoesNotThrow()
    {
        // Act
        var act = () => new GzipDecompressor(NullLogger<GzipDecompressor>.Instance);

        // Assert
        act.Should().NotThrow();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DecompressAsync — null guard
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DecompressAsync_WhenDataIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = async () => await sut.DecompressAsync(null!, null);

        // Assert
        await act.Should().ThrowExactlyAsync<ArgumentNullException>();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DecompressAsync — empty input
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DecompressAsync_WhenDataIsEmpty_ReturnsSameArrayAndWasCompressedFalse()
    {
        // Arrange
        var sut = CreateSut();
        var empty = Array.Empty<byte>();

        // Act
        var (bytes, wasCompressed) = await sut.DecompressAsync(empty, null);

        // Assert
        bytes.Should().BeSameAs(empty,
            because: "empty input must be returned as the exact same reference — no allocation");
        wasCompressed.Should().BeFalse();
    }

    [Fact]
    public async Task DecompressAsync_WhenDataIsEmptyWithGzipHeader_ReturnsSameArrayAndWasCompressedFalse()
    {
        // Arrange — even with the header, an empty payload should be returned as-is
        var sut = CreateSut();
        var empty = Array.Empty<byte>();

        // Act
        var (bytes, wasCompressed) = await sut.DecompressAsync(empty, "gzip");

        // Assert
        bytes.Should().BeSameAs(empty);
        wasCompressed.Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DecompressAsync — magic-byte detection (primary signal)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DecompressAsync_WhenMagicBytesPresent_DecompressesAndSetsWasCompressedTrue()
    {
        // Arrange
        const string original = "Hello, gzip world!";
        var sut = CreateSut();
        var compressed = Gzip(original);

        // Sanity: verify magic bytes are in place
        compressed[0].Should().Be(0x1F);
        compressed[1].Should().Be(0x8B);

        // Act
        var (bytes, wasCompressed) = await sut.DecompressAsync(compressed, null);

        // Assert
        wasCompressed.Should().BeTrue();
        Encoding.UTF8.GetString(bytes).Should().Be(original);
    }

    [Fact]
    public async Task DecompressAsync_WhenMagicBytesPresent_IgnoresNullContentEncodingHeader()
    {
        // Arrange — header null, but magic bytes are sufficient
        var sut = CreateSut();
        var compressed = Gzip("content without header");

        // Act
        var (bytes, wasCompressed) = await sut.DecompressAsync(compressed, contentEncodingHeader: null);

        // Assert
        wasCompressed.Should().BeTrue();
        Encoding.UTF8.GetString(bytes).Should().Be("content without header");
    }

    [Fact]
    public async Task DecompressAsync_WhenMagicBytesPresent_IgnoresIdentityHeader()
    {
        // Arrange — header says identity, but magic bytes override
        var sut = CreateSut();
        var compressed = Gzip("body text");

        // Act
        var (bytes, wasCompressed) = await sut.DecompressAsync(compressed, "identity");

        // Assert
        wasCompressed.Should().BeTrue();
        Encoding.UTF8.GetString(bytes).Should().Be("body text");
    }

    [Fact]
    public async Task DecompressAsync_WhenOnlyOneMagicBytePresentAndNoHeader_ReturnsOriginalBytes()
    {
        // Arrange — only 0x1F, not 0x8B; too short to be gzip
        var sut = CreateSut();
        var data = new byte[] { 0x1F, 0x00, 0x01, 0x02 };

        // Act
        var (bytes, wasCompressed) = await sut.DecompressAsync(data, null);

        // Assert
        wasCompressed.Should().BeFalse();
        bytes.Should().BeEquivalentTo(data);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DecompressAsync — Content-Encoding header detection (fallback signal)
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("gzip")]
    [InlineData("GZIP")]
    [InlineData("Gzip")]
    [InlineData("GZiP")]
    public async Task DecompressAsync_WhenContentEncodingIsGzip_CaseInsensitive_Decompresses(
        string headerValue)
    {
        // Arrange
        var sut = CreateSut();
        var compressed = Gzip("case-insensitive test");

        // Act
        var (bytes, wasCompressed) = await sut.DecompressAsync(compressed, headerValue);

        // Assert
        wasCompressed.Should().BeTrue();
        Encoding.UTF8.GetString(bytes).Should().Be("case-insensitive test");
    }

    [Theory]
    [InlineData("  gzip  ")]   // surrounding whitespace
    [InlineData("\tgzip\t")]   // tab-padded
    public async Task DecompressAsync_WhenContentEncodingHasSurroundingWhitespace_Decompresses(
        string headerValue)
    {
        // Arrange
        var sut = CreateSut();
        var compressed = Gzip("whitespace-trimmed header");

        // Act
        var (bytes, wasCompressed) = await sut.DecompressAsync(compressed, headerValue);

        // Assert
        wasCompressed.Should().BeTrue();
        Encoding.UTF8.GetString(bytes).Should().Be("whitespace-trimmed header");
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("br")]
    [InlineData("deflate")]
    [InlineData("chunked")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DecompressAsync_WhenContentEncodingIsNotGzipAndNoMagicBytes_ReturnsOriginal(
        string headerValue)
    {
        // Arrange — plain bytes, no magic, non-gzip header
        var sut = CreateSut();
        var data = Encoding.UTF8.GetBytes("plain text payload");

        // Act
        var (bytes, wasCompressed) = await sut.DecompressAsync(data, headerValue);

        // Assert
        wasCompressed.Should().BeFalse();
        bytes.Should().BeSameAs(data,
            because: "no gzip signal detected — original array reference must be returned");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DecompressAsync — no gzip signal at all
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DecompressAsync_WhenNoMagicBytesAndNoHeader_ReturnsOriginalAndWasCompressedFalse()
    {
        // Arrange
        var sut = CreateSut();
        var data = Encoding.UTF8.GetBytes("{\"key\":\"value\"}");

        // Act
        var (bytes, wasCompressed) = await sut.DecompressAsync(data, null);

        // Assert
        wasCompressed.Should().BeFalse();
        bytes.Should().BeSameAs(data);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DecompressAsync — decompressed content correctness
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DecompressAsync_WhenGzipData_ProducesIdenticalBytesToOriginalPlaintext()
    {
        // Arrange
        const string text = "The quick brown fox jumps over the lazy dog.";
        var sut = CreateSut();
        var original = Encoding.UTF8.GetBytes(text);
        var compressed = Gzip(text);

        // Act
        var (bytes, wasCompressed) = await sut.DecompressAsync(compressed, null);

        // Assert
        wasCompressed.Should().BeTrue();
        bytes.Should().BeEquivalentTo(original,
            because: "decompression must exactly reproduce the original byte sequence");
    }

    [Fact]
    public async Task DecompressAsync_WhenLargeGzipPayload_DecompressesCorrectly()
    {
        // Arrange — 500 KB of repeated text (highly compressible)
        var sut = CreateSut();
        var bigText = string.Concat(Enumerable.Repeat("ABCDEFGHIJ", 50_000));
        var compressed = Gzip(bigText);

        // Act
        var (bytes, wasCompressed) = await sut.DecompressAsync(compressed, null);

        // Assert
        wasCompressed.Should().BeTrue();
        bytes.Length.Should().Be(Encoding.UTF8.GetByteCount(bigText));
        Encoding.UTF8.GetString(bytes).Should().Be(bigText);
    }

    [Fact]
    public async Task DecompressAsync_WhenJsonPayload_DecompressesCorrectly()
    {
        // Arrange — realistic JSON body
        const string json = """{"userId":42,"name":"Alice","roles":["admin","viewer"]}""";
        var sut = CreateSut();
        var compressed = Gzip(json);

        // Act
        var (bytes, wasCompressed) = await sut.DecompressAsync(compressed, "gzip");

        // Assert
        wasCompressed.Should().BeTrue();
        Encoding.UTF8.GetString(bytes).Should().Be(json);
    }

    [Fact]
    public async Task DecompressAsync_WhenXmlPayload_DecompressesCorrectly()
    {
        // Arrange — realistic XML body
        const string xml = """<?xml version="1.0"?><root><item id="1">value</item></root>""";
        var sut = CreateSut();
        var compressed = Gzip(xml);

        // Act
        var (bytes, wasCompressed) = await sut.DecompressAsync(compressed, null);

        // Assert
        wasCompressed.Should().BeTrue();
        Encoding.UTF8.GetString(bytes).Should().Be(xml);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DecompressAsync — corrupt / invalid gzip data
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DecompressAsync_WhenMagicBytesButCorruptData_ThrowsInvalidDataException()
    {
        // Arrange — starts with gzip magic bytes but content is invalid
        var sut = CreateSut();
        var corrupt = new byte[] { 0x1F, 0x8B, 0xDE, 0xAD, 0xBE, 0xEF, 0x00 };

        // Act
        var act = async () => await sut.DecompressAsync(corrupt, null);

        // Assert — GZipStream propagates InvalidDataException for corrupt data
        await act.Should().ThrowAsync<InvalidDataException>(
            because: "corrupt gzip data must propagate InvalidDataException from GZipStream");
    }

    [Fact]
    public async Task DecompressAsync_WhenHeaderSaysGzipButDataIsNotGzip_ThrowsInvalidDataException()
    {
        // Arrange — header claims gzip but bytes are plain JSON (no magic)
        var sut = CreateSut();
        var plainJson = Encoding.UTF8.GetBytes("{\"key\":\"value\"}");

        // Act
        var act = async () => await sut.DecompressAsync(plainJson, "gzip");

        // Assert — decompression attempted but data is not a valid gzip stream
        await act.Should().ThrowAsync<InvalidDataException>(
            because: "Content-Encoding:gzip on non-gzip data must surface InvalidDataException");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DecompressAsync — cancellation
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DecompressAsync_WhenCancellationAlreadyRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var sut = CreateSut();
        var compressed = Gzip("some data");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await sut.DecompressAsync(compressed, null, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DecompressAsync — single-byte data edge cases
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DecompressAsync_WhenSingleByteAndNoHeader_ReturnsOriginalAndFalse()
    {
        // Arrange — one byte; cannot match both magic bytes
        var sut = CreateSut();
        var single = new byte[] { 0x1F };

        // Act
        var (bytes, wasCompressed) = await sut.DecompressAsync(single, null);

        // Assert
        wasCompressed.Should().BeFalse();
        bytes.Should().BeSameAs(single);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DecompressAsync — WasCompressed=false returns exact original reference
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DecompressAsync_WhenNotCompressed_ReturnsSameArrayReferenceWithoutAllocation()
    {
        // Arrange
        var sut = CreateSut();
        var original = Encoding.UTF8.GetBytes("not compressed");

        // Act
        var (bytes, wasCompressed) = await sut.DecompressAsync(original, null);

        // Assert — exact same object reference (no copy/allocation)
        wasCompressed.Should().BeFalse();
        bytes.Should().BeSameAs(original,
            because: "zero-copy: the original array reference must be returned unchanged");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DecompressAsync — concurrent calls (statelessness verification)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DecompressAsync_WhenCalledConcurrently_AllCallsSucceed()
    {
        // Arrange — Singleton must handle concurrent calls safely
        var sut = CreateSut();
        const int concurrency = 20;
        var payloads = Enumerable.Range(0, concurrency)
            .Select(i => Gzip($"payload-{i}"))
            .ToArray();

        // Act — fire all decompression tasks simultaneously
        var tasks = payloads.Select((p, i) =>
            sut.DecompressAsync(p, null)
               .ContinueWith(t => (Result: t.Result, Index: i)));

        var results = await Task.WhenAll(tasks);

        // Assert
        foreach (var (result, index) in results)
        {
            result.WasCompressed.Should().BeTrue();
            Encoding.UTF8.GetString(result.Bytes).Should().Be($"payload-{index}");
        }
    }
}
