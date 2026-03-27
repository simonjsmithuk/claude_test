using DataViewer.Application.Interfaces;
using DataViewer.Application.Services;
using DataViewer.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace DataViewer.Tests.Services;

/// <summary>
/// Unit tests for <see cref="BodyTruncator"/>.
/// Covers the full truncation matrix: streams smaller than, equal to, and larger
/// than the configured cap; fallback to 5 MB default when settings returns null
/// or an invalid value; repository exceptions; DI scope creation; and
/// cancellation propagation.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BodyTruncatorTests
{
    // ── Fixture helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="BodyTruncator"/> whose <see cref="ISystemSettingsRepository"/>
    /// is wired through a real <see cref="IServiceScopeFactory"/> so that
    /// <c>ResolveSizeCapMbAsync</c> resolves the mock repository from the scope.
    /// </summary>
    private static (BodyTruncator Sut, Mock<ISystemSettingsRepository> RepoMock)
        CreateSut(SystemSettings? settingsToReturn)
    {
        var repoMock = new Mock<ISystemSettingsRepository>();
        repoMock
            .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(settingsToReturn);

        var services = new ServiceCollection();
        services.AddScoped(_ => repoMock.Object);
        var provider = services.BuildServiceProvider();

        var sut = new BodyTruncator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BodyTruncator>.Instance);

        return (sut, repoMock);
    }

    /// <summary>
    /// Creates a <see cref="BodyTruncator"/> wired to a repository that throws
    /// the given exception on <c>GetAsync</c>.
    /// </summary>
    private static BodyTruncator CreateSutWithThrowingRepo(Exception exception)
    {
        var repoMock = new Mock<ISystemSettingsRepository>();
        repoMock
            .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var services = new ServiceCollection();
        services.AddScoped(_ => repoMock.Object);
        var provider = services.BuildServiceProvider();

        return new BodyTruncator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BodyTruncator>.Instance);
    }

    /// <summary>Creates a <see cref="MemoryStream"/> pre-filled with <paramref name="byteCount"/> bytes.</summary>
    private static MemoryStream MakeStream(int byteCount, byte fillValue = 0xAA)
    {
        var data = new byte[byteCount];
        Array.Fill(data, fillValue);
        return new MemoryStream(data);
    }

    /// <summary>Builds a <see cref="SystemSettings"/> with the given <see cref="SystemSettings.BodySizeCapMb"/>.</summary>
    private static SystemSettings SettingsWithCap(int capMb) => new() { BodySizeCapMb = capMb };

    // ═════════════════════════════════════════════════════════════════════════
    // Constructor guards
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_WhenScopeFactoryIsNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new BodyTruncator(
            null!,
            NullLogger<BodyTruncator>.Instance);

        // Assert
        act.Should().ThrowExactly<ArgumentNullException>()
           .WithParameterName("scopeFactory");
    }

    [Fact]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IServiceScopeFactory>();

        // Act
        var act = () => new BodyTruncator(factory, null!);

        // Assert
        act.Should().ThrowExactly<ArgumentNullException>()
           .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WhenAllDependenciesValid_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        // Act
        var act = () => new BodyTruncator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BodyTruncator>.Instance);

        // Assert
        act.Should().NotThrow();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TruncateIfNeededAsync — null stream guard
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TruncateIfNeededAsync_WhenStreamIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var (sut, _) = CreateSut(SettingsWithCap(1));

        // Act
        var act = async () => await sut.TruncateIfNeededAsync(null!);

        // Assert
        await act.Should().ThrowExactlyAsync<ArgumentNullException>();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TruncateIfNeededAsync — stream smaller than cap → not truncated
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TruncateIfNeededAsync_WhenStreamSmallerThanCap_ReturnsAllBytesAndIsTruncatedFalse()
    {
        // Arrange — 10 bytes stream, 1 MB cap
        const int capMb = 1;
        const int streamBytes = 10;
        var (sut, _) = CreateSut(SettingsWithCap(capMb));
        using var stream = MakeStream(streamBytes);

        // Act
        var (bytes, isTruncated) = await sut.TruncateIfNeededAsync(stream);

        // Assert
        bytes.Length.Should().Be(streamBytes);
        isTruncated.Should().BeFalse();
    }

    [Fact]
    public async Task TruncateIfNeededAsync_WhenStreamIsEmpty_ReturnEmptyBytesAndIsTruncatedFalse()
    {
        // Arrange
        var (sut, _) = CreateSut(SettingsWithCap(1));
        using var stream = new MemoryStream();

        // Act
        var (bytes, isTruncated) = await sut.TruncateIfNeededAsync(stream);

        // Assert
        bytes.Should().BeEmpty();
        isTruncated.Should().BeFalse();
    }

    [Fact]
    public async Task TruncateIfNeededAsync_WhenStreamIsOneByte_ReturnsOneByte()
    {
        // Arrange
        var (sut, _) = CreateSut(SettingsWithCap(1));
        using var stream = MakeStream(1);

        // Act
        var (bytes, isTruncated) = await sut.TruncateIfNeededAsync(stream);

        // Assert
        bytes.Should().HaveCount(1);
        isTruncated.Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TruncateIfNeededAsync — stream exactly at cap → not truncated
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task TruncateIfNeededAsync_WhenStreamExactlyAtCap_ReturnsAllBytesAndIsTruncatedFalse(
        int capMb)
    {
        // Arrange
        int capBytes = capMb * 1024 * 1024;
        var (sut, _) = CreateSut(SettingsWithCap(capMb));
        using var stream = MakeStream(capBytes);

        // Act
        var (bytes, isTruncated) = await sut.TruncateIfNeededAsync(stream);

        // Assert — exactly at cap means nothing was cut; IsTruncated must be false
        bytes.Length.Should().Be(capBytes,
            because: "a stream equal in length to the cap is NOT truncated");
        isTruncated.Should().BeFalse(
            because: "stream that fits exactly within the cap boundary is not truncated");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TruncateIfNeededAsync — stream exceeds cap → truncated
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TruncateIfNeededAsync_WhenStreamExceedsCap_ReturnsCappedBytesAndIsTruncatedTrue()
    {
        // Arrange — 1 MB cap, stream is 1 MB + 1 byte
        const int capMb = 1;
        int capBytes = capMb * 1024 * 1024;
        int streamBytes = capBytes + 1;
        var (sut, _) = CreateSut(SettingsWithCap(capMb));
        using var stream = MakeStream(streamBytes);

        // Act
        var (bytes, isTruncated) = await sut.TruncateIfNeededAsync(stream);

        // Assert
        bytes.Length.Should().Be(capBytes,
            because: "only up to the cap may be returned when the stream overflows");
        isTruncated.Should().BeTrue(
            because: "the extra probe byte was readable → stream had more data");
    }

    [Fact]
    public async Task TruncateIfNeededAsync_WhenStreamFarExceedsCap_ReturnsCappedBytesAndIsTruncatedTrue()
    {
        // Arrange — 1 MB cap, stream is 3 MB
        const int capMb = 1;
        int capBytes = capMb * 1024 * 1024;
        int streamBytes = capBytes * 3;
        var (sut, _) = CreateSut(SettingsWithCap(capMb));
        using var stream = MakeStream(streamBytes);

        // Act
        var (bytes, isTruncated) = await sut.TruncateIfNeededAsync(stream);

        // Assert
        bytes.Length.Should().Be(capBytes);
        isTruncated.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public async Task TruncateIfNeededAsync_WhenStreamExceedsDifferentCaps_AlwaysReturnsCappedLength(
        int capMb)
    {
        // Arrange — stream is always double the cap
        int capBytes = capMb * 1024 * 1024;
        var (sut, _) = CreateSut(SettingsWithCap(capMb));
        using var stream = MakeStream(capBytes * 2);

        // Act
        var (bytes, isTruncated) = await sut.TruncateIfNeededAsync(stream);

        // Assert
        bytes.Length.Should().Be(capBytes);
        isTruncated.Should().BeTrue();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TruncateIfNeededAsync — content integrity
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TruncateIfNeededAsync_WhenNotTruncated_ContentMatchesOriginalStream()
    {
        // Arrange — use distinguishable byte values
        var (sut, _) = CreateSut(SettingsWithCap(1));
        var data = Enumerable.Range(0, 100).Select(i => (byte)(i % 256)).ToArray();
        using var stream = new MemoryStream(data);

        // Act
        var (bytes, isTruncated) = await sut.TruncateIfNeededAsync(stream);

        // Assert
        isTruncated.Should().BeFalse();
        bytes.Should().BeEquivalentTo(data, options => options.WithStrictOrdering(),
            because: "byte content must be byte-for-byte identical to the stream data");
    }

    [Fact]
    public async Task TruncateIfNeededAsync_WhenTruncated_ReturnedBytesMatchFirstCapBytesOfStream()
    {
        // Arrange — fill stream with sequential values so we can assert which bytes were kept
        const int capMb = 1;
        int capBytes = capMb * 1024 * 1024;
        var data = Enumerable.Range(0, capBytes + 512).Select(i => (byte)(i % 256)).ToArray();
        var (sut, _) = CreateSut(SettingsWithCap(capMb));
        using var stream = new MemoryStream(data);

        // Act
        var (bytes, isTruncated) = await sut.TruncateIfNeededAsync(stream);

        // Assert
        isTruncated.Should().BeTrue();
        bytes.Length.Should().Be(capBytes);
        bytes.Should().BeEquivalentTo(data.Take(capBytes), options => options.WithStrictOrdering(),
            because: "only the first capBytes must be returned — the tail is discarded");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TruncateIfNeededAsync — size cap source: ISystemSettingsRepository
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TruncateIfNeededAsync_WhenRepositoryReturnsSettings_UsesConfiguredCap()
    {
        // Arrange — cap = 2 MB, stream = 2 MB + 1 byte
        const int capMb = 2;
        int capBytes = capMb * 1024 * 1024;
        var (sut, repoMock) = CreateSut(SettingsWithCap(capMb));
        using var stream = MakeStream(capBytes + 1);

        // Act
        var (bytes, isTruncated) = await sut.TruncateIfNeededAsync(stream);

        // Assert
        bytes.Length.Should().Be(capBytes,
            because: $"the configured cap of {capMb} MB must be honoured");
        isTruncated.Should().BeTrue();

        // Verify repository was consulted
        repoMock.Verify(r => r.GetAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TruncateIfNeededAsync_WhenRepositoryReturnsNull_FallsBackToDefault5Mb()
    {
        // Arrange — null settings row (unseeded database)
        const int defaultCapBytes = BodyTruncator.DefaultBodySizeCapMb * 1024 * 1024;
        var (sut, _) = CreateSut(settingsToReturn: null);

        // Stream is exactly defaultCap + 1 so we can confirm the 5 MB cap was used
        using var stream = MakeStream(defaultCapBytes + 1);

        // Act
        var (bytes, isTruncated) = await sut.TruncateIfNeededAsync(stream);

        // Assert
        bytes.Length.Should().Be(defaultCapBytes,
            because: "null settings must fall back to the 5 MB default");
        isTruncated.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task TruncateIfNeededAsync_WhenConfiguredCapIsInvalid_FallsBackToDefault5Mb(
        int invalidCapMb)
    {
        // Arrange
        const int defaultCapBytes = BodyTruncator.DefaultBodySizeCapMb * 1024 * 1024;
        var (sut, _) = CreateSut(SettingsWithCap(invalidCapMb));
        using var stream = MakeStream(defaultCapBytes + 1);

        // Act
        var (bytes, isTruncated) = await sut.TruncateIfNeededAsync(stream);

        // Assert
        bytes.Length.Should().Be(defaultCapBytes,
            because: $"cap={invalidCapMb} is invalid (< 1) → must fall back to 5 MB default");
        isTruncated.Should().BeTrue();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TruncateIfNeededAsync — repository exception resilience
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TruncateIfNeededAsync_WhenRepositoryThrowsGenericException_FallsBackToDefault5MbAndDoesNotThrow()
    {
        // Arrange — simulate transient DB failure
        const int defaultCapBytes = BodyTruncator.DefaultBodySizeCapMb * 1024 * 1024;
        var sut = CreateSutWithThrowingRepo(new InvalidOperationException("transient DB failure"));
        using var stream = MakeStream(100); // small stream, well within any cap

        // Act
        var act = async () => await sut.TruncateIfNeededAsync(stream);

        // Assert — must not propagate; falls back to default silently
        await act.Should().NotThrowAsync(
            because: "a repository exception must be caught and the default cap applied");

        // Re-run to get the actual result
        stream.Position = 0;
        var (bytes, isTruncated) = await sut.TruncateIfNeededAsync(stream);
        bytes.Length.Should().Be(100);
        isTruncated.Should().BeFalse();
    }

    [Fact]
    public async Task TruncateIfNeededAsync_WhenRepositoryThrowsTimeoutException_FallsBackToDefault()
    {
        // Arrange
        var sut = CreateSutWithThrowingRepo(new TimeoutException("DB timeout"));
        using var stream = MakeStream(50);

        // Act
        var (bytes, isTruncated) = await sut.TruncateIfNeededAsync(stream);

        // Assert
        bytes.Length.Should().Be(50);
        isTruncated.Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TruncateIfNeededAsync — cancellation
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TruncateIfNeededAsync_WhenCancellationAlreadyRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var (sut, _) = CreateSut(SettingsWithCap(1));
        using var stream = MakeStream(100);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await sut.TruncateIfNeededAsync(stream, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TruncateIfNeededAsync_WhenRepositoryThrowsOperationCanceled_Propagates()
    {
        // Arrange — OperationCanceledException must NOT be swallowed by the catch block
        var repoMock = new Mock<ISystemSettingsRepository>();
        repoMock
            .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("cancelled"));

        var services = new ServiceCollection();
        services.AddScoped(_ => repoMock.Object);
        var provider = services.BuildServiceProvider();

        var sut = new BodyTruncator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BodyTruncator>.Instance);

        using var stream = MakeStream(100);

        // Act
        var act = async () => await sut.TruncateIfNeededAsync(stream);

        // Assert — OCE is re-thrown; not swallowed like other exceptions
        await act.Should().ThrowAsync<OperationCanceledException>(
            because: "OperationCanceledException must propagate — the catch block has 'when (ex is not OperationCanceledException)'");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TruncateIfNeededAsync — multiple sequential calls (statelessness)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TruncateIfNeededAsync_WhenCalledMultipleTimes_EachCallRespectsFreshCapFromRepo()
    {
        // Arrange — repository returns different caps on each call to simulate an admin change
        var repoMock = new Mock<ISystemSettingsRepository>();
        var callCount = 0;
        repoMock
            .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                // First call: 1 MB cap; second call: 2 MB cap
                return new SystemSettings { BodySizeCapMb = callCount == 1 ? 1 : 2 };
            });

        var services = new ServiceCollection();
        services.AddScoped(_ => repoMock.Object);
        var provider = services.BuildServiceProvider();

        var sut = new BodyTruncator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BodyTruncator>.Instance);

        // First call: 1 MB cap, stream = 2 MB → truncated to 1 MB
        int firstCapBytes = 1 * 1024 * 1024;
        using var stream1 = MakeStream(firstCapBytes * 2);
        var (bytes1, isTruncated1) = await sut.TruncateIfNeededAsync(stream1);

        // Second call: 2 MB cap, stream = 1 MB → not truncated
        int secondCapBytes = 2 * 1024 * 1024;
        using var stream2 = MakeStream(firstCapBytes); // only 1 MB
        var (bytes2, isTruncated2) = await sut.TruncateIfNeededAsync(stream2);

        // Assert first call
        bytes1.Length.Should().Be(firstCapBytes);
        isTruncated1.Should().BeTrue();

        // Assert second call — cap was refreshed from repo
        bytes2.Length.Should().Be(firstCapBytes);
        isTruncated2.Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TruncateIfNeededAsync — default constant
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DefaultBodySizeCapMb_IsEqualToFive()
    {
        // The acceptance criteria mandate a 5 MB default.
        BodyTruncator.DefaultBodySizeCapMb.Should().Be(5,
            because: "TASK-018 acceptance criteria explicitly require a 5 MB fallback default");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TruncateIfNeededAsync — forward-only / non-seekable streams
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TruncateIfNeededAsync_WhenStreamIsNonSeekable_WorksCorrectly()
    {
        // Arrange — wrap a MemoryStream in a non-seekable adapter
        const int capMb = 1;
        int capBytes = capMb * 1024 * 1024;
        var (sut, _) = CreateSut(SettingsWithCap(capMb));

        var data = new byte[capBytes + 100];
        Array.Fill(data, (byte)0xBB);
        var backingStream = new MemoryStream(data);
        using var nonSeekable = new NonSeekableStreamWrapper(backingStream);

        // Act
        var (bytes, isTruncated) = await sut.TruncateIfNeededAsync(nonSeekable);

        // Assert
        bytes.Length.Should().Be(capBytes);
        isTruncated.Should().BeTrue(
            because: "the probe-byte technique must work without seeking");
    }

    [Fact]
    public async Task TruncateIfNeededAsync_WhenNonSeekableStreamSmallerThanCap_ReturnsAllBytes()
    {
        // Arrange
        var (sut, _) = CreateSut(SettingsWithCap(1));
        const int streamSize = 512;
        var data = new byte[streamSize];
        Array.Fill(data, (byte)0xCC);
        using var nonSeekable = new NonSeekableStreamWrapper(new MemoryStream(data));

        // Act
        var (bytes, isTruncated) = await sut.TruncateIfNeededAsync(nonSeekable);

        // Assert
        bytes.Length.Should().Be(streamSize);
        isTruncated.Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TruncateIfNeededAsync — DI scope is created per call
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TruncateIfNeededAsync_WhenCalledTwice_CreatesANewDiScopeEachTime()
    {
        // Arrange — track how many times the repo is resolved (one scope per call)
        var resolutionCount = 0;
        var services = new ServiceCollection();
        services.AddScoped<ISystemSettingsRepository>(_ =>
        {
            resolutionCount++;
            var mock = new Mock<ISystemSettingsRepository>();
            mock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SystemSettings { BodySizeCapMb = 1 });
            return mock.Object;
        });

        var provider = services.BuildServiceProvider();
        var sut = new BodyTruncator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BodyTruncator>.Instance);

        using var s1 = MakeStream(10);
        using var s2 = MakeStream(10);

        // Act
        await sut.TruncateIfNeededAsync(s1);
        await sut.TruncateIfNeededAsync(s2);

        // Assert — each call creates one scope → resolves the repo once per call
        resolutionCount.Should().Be(2,
            because: "a new DI scope (and therefore a fresh repository instance) "
                   + "must be created on every TruncateIfNeededAsync invocation");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Inner test helper — NonSeekableStreamWrapper
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Wraps a <see cref="Stream"/> and makes it appear non-seekable so that tests
    /// can verify the truncator does not rely on <see cref="Stream.Seek"/>.
    /// </summary>
    private sealed class NonSeekableStreamWrapper : Stream
    {
        private readonly Stream _inner;

        public NonSeekableStreamWrapper(Stream inner) => _inner = inner;

        public override bool CanRead  => true;
        public override bool CanSeek  => false;   // ← key: non-seekable
        public override bool CanWrite => false;
        public override long Length   => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override void Flush()         => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
