using Amazon;
using Amazon.S3;
using DataViewer.Domain.Entities;
using DataViewer.Infrastructure.S3;
using DataViewer.Tests.S3.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataViewer.Tests.S3;

/// <summary>
/// Unit tests for <see cref="S3ClientFactory"/>.
/// </summary>
/// <remarks>
/// Because <see cref="S3ClientFactory"/> is declared <c>internal sealed</c>, the
/// <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> is added
/// to the Infrastructure project to expose it to this test project.
/// All tests use the InternalsVisibleTo friend-assembly access already granted by
/// the existing test project reference in the solution.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class S3ClientFactoryTests
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly S3ClientFactory _sut;

    public S3ClientFactoryTests()
    {
        _sut = new S3ClientFactory(NullLogger<S3ClientFactory>.Instance);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Constructor
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new S3ClientFactory(null!);

        // Assert
        act.Should().ThrowExactly<ArgumentNullException>()
           .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WhenLoggerIsValid_DoesNotThrow()
    {
        // Act
        var act = () => new S3ClientFactory(NullLogger<S3ClientFactory>.Instance);

        // Assert
        act.Should().NotThrow();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Create — happy path
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Create_WhenValidProfileAndKey_ReturnsAmazonS3Client()
    {
        // Arrange
        var profile = CredentialProfileBuilder.AValid().Build();

        // Act
        using var client = _sut.Create(profile, "validSecretKey");

        // Assert
        client.Should().NotBeNull();
        client.Should().BeOfType<AmazonS3Client>();
    }

    [Fact]
    public void Create_WhenCalled_ReturnsNewInstanceEachTime()
    {
        // Arrange
        var profile = CredentialProfileBuilder.AValid().Build();

        // Act
        using var client1 = _sut.Create(profile, "validSecretKey");
        using var client2 = _sut.Create(profile, "validSecretKey");

        // Assert — NOT a singleton; each call produces a distinct object
        client1.Should().NotBeSameAs(client2,
            because: "AmazonS3Client must never be a singleton — each call must produce a new instance");
    }

    [Theory]
    [InlineData("us-east-1")]
    [InlineData("eu-west-2")]
    [InlineData("ap-southeast-1")]
    [InlineData("us-west-2")]
    [InlineData("ca-central-1")]
    public void Create_WhenKnownRegions_ReturnsClientWithoutThrowing(string region)
    {
        // Arrange
        var profile = CredentialProfileBuilder.AValid().WithRegion(region).Build();

        // Act
        var act = () =>
        {
            using var c = _sut.Create(profile, "someSecretKey");
        };

        // Assert
        act.Should().NotThrow(
            because: $"region '{region}' is a known valid AWS region");
    }

    [Fact]
    public void Create_WhenUnknownRegion_StillReturnsClientViaFallback()
    {
        // Arrange — AWS SDK generates an endpoint for unknown region strings
        var profile = CredentialProfileBuilder.AValid().WithRegion("xx-unknown-99").Build();

        // Act
        var act = () =>
        {
            using var c = _sut.Create(profile, "someSecretKey");
        };

        // Assert — the SDK does not throw for unknown regions; it falls back to
        // a generated endpoint (RegionEndpoint.GetBySystemName contract).
        act.Should().NotThrow(
            because: "AWS SDK accepts unknown region strings via RegionEndpoint.GetBySystemName fallback");
    }

    [Fact]
    public void Create_ReturnedClient_IsDisposable()
    {
        // Arrange
        var profile = CredentialProfileBuilder.AValid().Build();

        // Act
        var client = _sut.Create(profile, "validSecretKey");

        // Assert — verify IDisposable is implemented (required by acceptance criteria)
        client.Should().BeAssignableTo<IDisposable>(
            because: "the caller must be able to dispose the client via a using block");

        // Cleanup
        client.Dispose();
    }

    [Fact]
    public void Create_WhenCalledWithDifferentProfiles_ReturnsSeparateClientInstances()
    {
        // Arrange — two different profiles with different credentials
        var profileA = CredentialProfileBuilder.AValid()
            .WithAccessKeyId("AKIAIOSFODNN7AAAAAAA")
            .WithRegion("us-east-1")
            .Build();

        var profileB = CredentialProfileBuilder.AValid()
            .WithAccessKeyId("AKIAIOSFODNN7BBBBBBB")
            .WithRegion("eu-west-2")
            .Build();

        // Act
        using var clientA = _sut.Create(profileA, "secretKeyForA");
        using var clientB = _sut.Create(profileB, "secretKeyForB");

        // Assert — credential isolation: each profile gets its own independent client
        clientA.Should().NotBeSameAs(clientB,
            because: "credential isolation requires one client per profile, never shared");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Create — null / empty argument guards
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Create_WhenProfileIsNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _sut.Create(null!, "validSecretKey");

        // Assert
        act.Should().ThrowExactly<ArgumentNullException>()
           .WithParameterName("profile");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenDecryptedSecretKeyIsNullOrWhiteSpace_ThrowsArgumentException(
        string? badKey)
    {
        // Arrange
        var profile = CredentialProfileBuilder.AValid().Build();

        // Act
        var act = () => _sut.Create(profile, badKey!);

        // Assert
        act.Should().Throw<ArgumentException>(
            because: "a null/empty/whitespace secret key must be rejected before creating the client");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenAccessKeyIdIsNullOrWhiteSpace_ThrowsArgumentException(
        string? badAccessKey)
    {
        // Arrange
        var profile = CredentialProfileBuilder.AValid().Build();
        profile.AccessKeyId = badAccessKey!;

        // Act
        var act = () => _sut.Create(profile, "validSecretKey");

        // Assert
        act.Should().Throw<ArgumentException>(
            because: "a profile with a missing AccessKeyId must be rejected");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenRegionIsNullOrWhiteSpace_ThrowsArgumentException(
        string? badRegion)
    {
        // Arrange
        var profile = CredentialProfileBuilder.AValid().Build();
        profile.Region = badRegion!;

        // Act
        var act = () => _sut.Create(profile, "validSecretKey");

        // Assert
        act.Should().Throw<ArgumentException>(
            because: "a profile with a missing Region must be rejected");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Secret-key logging safety
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Create_SecretKey_NeverAppearsInLogOutput()
    {
        // Arrange — capture log messages via a test logger sink
        const string sensitiveSecret = "MY_VERY_SECRET_AWS_KEY_12345";
        var logSink = new CapturingLogger<S3ClientFactory>();
        var factoryWithSink = new S3ClientFactory(logSink);
        var profile = CredentialProfileBuilder.AValid().Build();

        // Act
        using var client = factoryWithSink.Create(profile, sensitiveSecret);

        // Assert — the secret must never appear anywhere in structured log output
        logSink.Messages.Should().NotContain(
            msg => msg.Contains(sensitiveSecret),
            because: "decrypted secret access keys must NEVER be written to any log output");
    }

    [Fact]
    public void Create_AccessKeyId_AppearsSafelyInLogOutput()
    {
        // Arrange
        const string accessKeyId = "AKIAIOSFODNN7EXAMPLE";
        var logSink = new CapturingLogger<S3ClientFactory>();
        var factoryWithSink = new S3ClientFactory(logSink);
        var profile = CredentialProfileBuilder.AValid().WithAccessKeyId(accessKeyId).Build();

        // Act
        using var client = factoryWithSink.Create(profile, "someSecretKey");

        // Assert — the public access key ID IS expected in logs (it's not sensitive)
        logSink.Messages.Should().Contain(
            msg => msg.Contains(accessKeyId),
            because: "the non-sensitive AccessKeyId should appear in diagnostic log output");
    }
}
