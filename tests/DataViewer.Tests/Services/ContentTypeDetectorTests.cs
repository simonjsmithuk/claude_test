using System.Text;
using DataViewer.Application.Services;
using DataViewer.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataViewer.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ContentTypeDetector"/>.
/// Covers header-based fast-path, byte-sniff logic, whitespace trimming, all
/// <see cref="BodyContentType"/> values, and all edge / boundary conditions
/// described in the TASK-018 acceptance criteria.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ContentTypeDetectorTests
{
    // ── System-under-test factory ─────────────────────────────────────────────

    private static ContentTypeDetector CreateSut() =>
        new(NullLogger<ContentTypeDetector>.Instance);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    // ═════════════════════════════════════════════════════════════════════════
    // Constructor guard
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new ContentTypeDetector(null!);

        // Assert
        act.Should().ThrowExactly<ArgumentNullException>()
           .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WhenLoggerIsValid_DoesNotThrow()
    {
        // Act
        var act = () => new ContentTypeDetector(NullLogger<ContentTypeDetector>.Instance);

        // Assert
        act.Should().NotThrow();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Detect — null / empty body
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Detect_WhenBodyBytesIsNull_ReturnsUnknown()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.Detect(null, null);

        // Assert
        result.Should().Be(BodyContentType.Unknown);
    }

    [Fact]
    public void Detect_WhenBodyBytesIsEmpty_ReturnsUnknown()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.Detect(Array.Empty<byte>(), null);

        // Assert
        result.Should().Be(BodyContentType.Unknown);
    }

    [Fact]
    public void Detect_WhenBodyBytesIsEmptyWithJsonHeader_StillReturnsUnknown()
    {
        // Arrange — even a conclusive header must not override the empty-body guard
        var sut = CreateSut();

        // Act
        var result = sut.Detect(Array.Empty<byte>(), "application/json");

        // Assert
        result.Should().Be(BodyContentType.Unknown,
            because: "empty body must return Unknown regardless of any header value");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Detect — header-based fast path (step 1)
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("text/json")]
    [InlineData("APPLICATION/JSON")]            // upper-case
    [InlineData("Application/Json")]            // mixed-case
    [InlineData("application/vnd.api+json")]    // vendor media type
    public void Detect_WhenHeaderContainsJson_ReturnsJson(string header)
    {
        // Arrange — any body bytes that would byte-sniff as Text (to prove header wins)
        var sut = CreateSut();
        var body = Bytes("plain text that byte-sniff would classify as Text");

        // Act
        var result = sut.Detect(body, header);

        // Assert
        result.Should().Be(BodyContentType.Json,
            because: $"Content-Type '{header}' contains 'json' → header fast-path must win");
    }

    [Theory]
    [InlineData("application/xml")]
    [InlineData("application/xml; charset=utf-8")]
    [InlineData("text/xml")]
    [InlineData("APPLICATION/XML")]
    [InlineData("image/svg+xml")]
    public void Detect_WhenHeaderContainsXml_ReturnsXml(string header)
    {
        // Arrange
        var sut = CreateSut();
        var body = Bytes("plain text body");

        // Act
        var result = sut.Detect(body, header);

        // Assert
        result.Should().Be(BodyContentType.Xml,
            because: $"Content-Type '{header}' contains 'xml' → header fast-path must return Xml");
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("text/html; charset=utf-8")]
    [InlineData("TEXT/HTML")]
    [InlineData("application/xhtml+xml")]   // contains both 'html' (no, 'xhtml') and 'xml'
    public void Detect_WhenHeaderContainsHtml_ReturnsXml(string header)
    {
        // Arrange — spec maps html header → Xml
        var sut = CreateSut();
        var body = Bytes("plain text body");

        // Act
        var result = sut.Detect(body, header);

        // Assert
        result.Should().Be(BodyContentType.Xml,
            because: $"Content-Type '{header}' contains 'html' → spec maps it to Xml");
    }

    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("text/plain")]
    [InlineData("multipart/form-data")]
    [InlineData("image/png")]
    [InlineData("application/pdf")]
    public void Detect_WhenHeaderIsInconclusiveAndBodyStartsWithBrace_ReturnsJson(string header)
    {
        // Arrange — inconclusive header; byte-sniff should win
        var sut = CreateSut();
        var body = Bytes("""{"key":"value"}""");

        // Act
        var result = sut.Detect(body, header);

        // Assert
        result.Should().Be(BodyContentType.Json,
            because: "header inconclusive → byte-sniff finds '{' → Json");
    }

    [Fact]
    public void Detect_WhenHeaderIsNullAndBodyStartsWithBrace_ReturnsJson()
    {
        // Arrange
        var sut = CreateSut();
        var body = Bytes("""{"id":1}""");

        // Act
        var result = sut.Detect(body, null);

        // Assert
        result.Should().Be(BodyContentType.Json);
    }

    [Fact]
    public void Detect_WhenHeaderIsWhitespaceOnly_FallsThroughToByteSniff()
    {
        // Arrange
        var sut = CreateSut();
        var body = Bytes("[1,2,3]");

        // Act
        var result = sut.Detect(body, "   ");

        // Assert
        result.Should().Be(BodyContentType.Json,
            because: "whitespace-only header is treated as absent → byte-sniff applies");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Detect — byte-sniff: JSON (step 2a — '{' or '[')
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("""{"key":"value"}""")]
    [InlineData("""{"nested":{"a":1}}""")]
    [InlineData("{}")]                              // empty object
    [InlineData("""{  "spaced"  : true  }""")]     // spaced-out JSON object
    public void Detect_WhenBodyStartsWithOpenBrace_ReturnsJson(string jsonText)
    {
        // Arrange
        var sut = CreateSut();
        var body = Bytes(jsonText);

        // Act
        var result = sut.Detect(body, null);

        // Assert
        result.Should().Be(BodyContentType.Json);
    }

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("[]")]                  // empty array
    [InlineData("""[{"a":1},{"b":2}]""")]
    [InlineData("[true, false, null]")]
    public void Detect_WhenBodyStartsWithOpenBracket_ReturnsJson(string jsonText)
    {
        // Arrange
        var sut = CreateSut();
        var body = Bytes(jsonText);

        // Act
        var result = sut.Detect(body, null);

        // Assert
        result.Should().Be(BodyContentType.Json);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Detect — byte-sniff: XML (step 2b — '<' or '<?xml')
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Detect_WhenBodyStartsWithXmlDeclaration_ReturnsXml()
    {
        // Arrange
        var sut = CreateSut();
        var body = Bytes("""<?xml version="1.0" encoding="UTF-8"?><root/>""");

        // Act
        var result = sut.Detect(body, null);

        // Assert
        result.Should().Be(BodyContentType.Xml);
    }

    [Theory]
    [InlineData("<root><child/></root>")]
    [InlineData("<html><body>hello</body></html>")]
    [InlineData("<item id=\"1\">value</item>")]
    [InlineData("<")]                               // single open bracket
    public void Detect_WhenBodyStartsWithAngleBracket_ReturnsXml(string markup)
    {
        // Arrange
        var sut = CreateSut();
        var body = Bytes(markup);

        // Act
        var result = sut.Detect(body, null);

        // Assert
        result.Should().Be(BodyContentType.Xml,
            because: "spec: '<' → Xml regardless of whether it's an xml declaration or plain tag");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Detect — byte-sniff: Text (step 2c — anything else)
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("plain text content")]
    [InlineData("Hello World")]
    [InlineData("some arbitrary string")]
    [InlineData("123456")]                  // starts with digit
    [InlineData("-1.5")]                    // starts with minus sign
    [InlineData("true")]                    // bare boolean (not wrapped in JSON brackets)
    [InlineData("null")]                    // bare null
    public void Detect_WhenBodyStartsWithNonSpecialCharacter_ReturnsText(string text)
    {
        // Arrange
        var sut = CreateSut();
        var body = Bytes(text);

        // Act
        var result = sut.Detect(body, null);

        // Assert
        result.Should().Be(BodyContentType.Text);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Detect — leading ASCII whitespace trimming
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(" {\"key\":\"val\"}")]        // leading space
    [InlineData("\t{\"key\":\"val\"}")]       // leading tab
    [InlineData("\r\n{\"key\":\"val\"}")]     // leading CRLF
    [InlineData("\n{\"key\":\"val\"}")]       // leading LF
    [InlineData("   \t\r\n{\"a\":1}")]       // mixed whitespace
    public void Detect_WhenBodyHasLeadingWhitespaceBeforeOpenBrace_ReturnsJson(string jsonText)
    {
        // Arrange
        var sut = CreateSut();
        var body = Bytes(jsonText);

        // Act
        var result = sut.Detect(body, null);

        // Assert
        result.Should().Be(BodyContentType.Json,
            because: "leading ASCII whitespace must be skipped before inspecting the first content byte");
    }

    [Theory]
    [InlineData(" [1,2,3]")]
    [InlineData("\t[1,2,3]")]
    [InlineData("\r\n[1,2,3]")]
    [InlineData("   \t\n[1,2,3]")]
    public void Detect_WhenBodyHasLeadingWhitespaceBeforeOpenBracket_ReturnsJson(string text)
    {
        // Arrange
        var sut = CreateSut();
        var body = Bytes(text);

        // Act
        var result = sut.Detect(body, null);

        // Assert
        result.Should().Be(BodyContentType.Json);
    }

    [Theory]
    [InlineData(" <root/>")]
    [InlineData("\n<root/>")]
    [InlineData("\r\n<root/>")]
    [InlineData("   <?xml version=\"1.0\"?><root/>")]
    public void Detect_WhenBodyHasLeadingWhitespaceBeforeAngleBracket_ReturnsXml(string text)
    {
        // Arrange
        var sut = CreateSut();
        var body = Bytes(text);

        // Act
        var result = sut.Detect(body, null);

        // Assert
        result.Should().Be(BodyContentType.Xml);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Detect — whitespace-only body
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData("\n\n\n")]
    [InlineData("   \t\r\n   ")]
    public void Detect_WhenBodyContainsOnlyWhitespace_ReturnsText(string whitespace)
    {
        // Arrange — body has bytes but all are whitespace
        var sut = CreateSut();
        var body = Bytes(whitespace);

        // Act
        var result = sut.Detect(body, null);

        // Assert
        result.Should().Be(BodyContentType.Text,
            because: "a whitespace-only body is treated as plain Text after trimming");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Detect — binary / non-text body
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Detect_WhenBodyStartsWithNullByte_ReturnsText()
    {
        // Arrange — binary-style data
        var sut = CreateSut();
        var body = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        // Act
        var result = sut.Detect(body, null);

        // Assert
        result.Should().Be(BodyContentType.Text,
            because: "binary data that doesn't match any signal must fall through to Text");
    }

    [Fact]
    public void Detect_WhenBodyStartsWithGzipMagicBytes_ReturnsText()
    {
        // Arrange — raw gzip bytes (not decompressed); 0x1F is not a JSON/XML starter
        var sut = CreateSut();
        var body = new byte[] { 0x1F, 0x8B, 0x08, 0x00 };

        // Act
        var result = sut.Detect(body, null);

        // Assert
        result.Should().Be(BodyContentType.Text,
            because: "gzip magic bytes (0x1F, 0x8B) do not match JSON or XML starters");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Detect — single-byte bodies
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Detect_WhenBodyIsSingleOpenBrace_ReturnsJson()
    {
        var sut = CreateSut();
        var result = sut.Detect(new byte[] { (byte)'{' }, null);
        result.Should().Be(BodyContentType.Json);
    }

    [Fact]
    public void Detect_WhenBodyIsSingleOpenBracket_ReturnsJson()
    {
        var sut = CreateSut();
        var result = sut.Detect(new byte[] { (byte)'[' }, null);
        result.Should().Be(BodyContentType.Json);
    }

    [Fact]
    public void Detect_WhenBodyIsSingleAngleBracket_ReturnsXml()
    {
        var sut = CreateSut();
        var result = sut.Detect(new byte[] { (byte)'<' }, null);
        result.Should().Be(BodyContentType.Xml);
    }

    [Fact]
    public void Detect_WhenBodyIsSingleCharacterOther_ReturnsText()
    {
        var sut = CreateSut();
        var result = sut.Detect(new byte[] { (byte)'A' }, null);
        result.Should().Be(BodyContentType.Text);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Detect — header takes priority over byte sniff
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Detect_WhenHeaderSaysJsonButBodyLooksLikeXml_ReturnsJson()
    {
        // Arrange — conflicting signals; header wins
        var sut = CreateSut();
        var body = Bytes("<root/>"); // byte-sniff would say Xml

        // Act
        var result = sut.Detect(body, "application/json");

        // Assert
        result.Should().Be(BodyContentType.Json,
            because: "header-based detection takes precedence over byte-sniffing");
    }

    [Fact]
    public void Detect_WhenHeaderSaysXmlButBodyLooksLikeJson_ReturnsXml()
    {
        // Arrange — conflicting signals; header wins
        var sut = CreateSut();
        var body = Bytes("""{"key":"value"}"""); // byte-sniff would say Json

        // Act
        var result = sut.Detect(body, "text/xml");

        // Assert
        result.Should().Be(BodyContentType.Xml,
            because: "header-based detection takes precedence over byte-sniffing");
    }

    [Fact]
    public void Detect_WhenHeaderSaysHtmlButBodyLooksLikeJson_ReturnsXml()
    {
        // Arrange
        var sut = CreateSut();
        var body = Bytes("""[1,2,3]""");

        // Act
        var result = sut.Detect(body, "text/html");

        // Assert
        result.Should().Be(BodyContentType.Xml,
            because: "html header maps to Xml per spec, and header beats byte-sniff");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Detect — statelessness / concurrent call safety
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Detect_WhenCalledConcurrently_AllCallsReturnCorrectResults()
    {
        // Arrange
        var sut = CreateSut();
        var inputs = new (byte[] Body, string? Header, BodyContentType Expected)[]
        {
            (Bytes("""{"a":1}"""),           null,               BodyContentType.Json),
            (Bytes("[1,2,3]"),               null,               BodyContentType.Json),
            (Bytes("<root/>"),               null,               BodyContentType.Xml),
            (Bytes("plain text"),            null,               BodyContentType.Text),
            (Bytes("anything"),              "application/json", BodyContentType.Json),
            (Bytes("anything"),              "text/xml",         BodyContentType.Xml),
        };

        // Act — run all detections in parallel
        var results = inputs
            .AsParallel()
            .Select(i => (Expected: i.Expected, Actual: sut.Detect(i.Body, i.Header)))
            .ToList();

        // Assert
        foreach (var (expected, actual) in results)
        {
            actual.Should().Be(expected);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Detect — realistic multi-line JSON / XML bodies
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Detect_WhenPrettyPrintedJsonBody_ReturnsJson()
    {
        // Arrange
        var sut = CreateSut();
        var json = """
            {
              "id": 1,
              "name": "Alice",
              "roles": ["admin"]
            }
            """;
        var body = Bytes(json);

        // Act
        var result = sut.Detect(body, null);

        // Assert
        result.Should().Be(BodyContentType.Json);
    }

    [Fact]
    public void Detect_WhenPrettyPrintedXmlBody_ReturnsXml()
    {
        // Arrange
        var sut = CreateSut();
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <response>
              <status>200</status>
            </response>
            """;
        var body = Bytes(xml.TrimStart()); // leading newline trimmed at source to keep first char as '<'

        // Act
        var result = sut.Detect(body, null);

        // Assert
        result.Should().Be(BodyContentType.Xml);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Detect — UTF-8 BOM handling
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Detect_WhenBodyHasUtf8BomBeforeJsonContent_ReturnsText()
    {
        // Arrange — UTF-8 BOM is 0xEF 0xBB 0xBF; not a recognised whitespace byte,
        // so the BOM byte itself becomes the "first non-whitespace byte" → Text
        var sut = CreateSut();
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var json = Bytes("""{"key":"value"}""");
        var body = bom.Concat(json).ToArray();

        // Act
        var result = sut.Detect(body, null);

        // Assert — BOM is treated as an unrecognised byte → Text
        // (the implementation only strips ASCII whitespace, not BOM)
        result.Should().Be(BodyContentType.Text);
    }

    [Fact]
    public void Detect_WhenBodyHasUtf8BomAndHeaderSaysJson_ReturnsJson()
    {
        // Arrange — header saves the day when BOM confuses byte-sniff
        var sut = CreateSut();
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var json = Bytes("""{"key":"value"}""");
        var body = bom.Concat(json).ToArray();

        // Act
        var result = sut.Detect(body, "application/json");

        // Assert — header bypasses byte-sniff entirely
        result.Should().Be(BodyContentType.Json);
    }
}
