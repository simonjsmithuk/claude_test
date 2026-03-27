using DataViewer.Application.DTOs.AuditLogs;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Entities;
using DataViewer.Domain.Enums;
using DataViewer.Domain.Exceptions;
using DataViewer.Infrastructure.Persistence;
using DataViewer.Infrastructure.Persistence.Repositories;
using DataViewer.Tests.Persistence.Repositories.Helpers;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace DataViewer.Tests.Persistence.Repositories;

/// <summary>
/// Unit and integration tests for <see cref="AuditRepository.InsertAsync"/>.
/// </summary>
/// <remarks>
/// Tests use a SQLite in-memory database via <see cref="AuditTestDbContextFactory"/>
/// so that EF Core operations (Add, SaveChanges) execute against a real relational
/// provider rather than the non-relational in-memory provider.
/// External dependencies (<see cref="IDbContextFactory{TContext}"/>) are mocked
/// where full isolation from the write context is required.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class AuditRepositoryInsertAsyncTests : IAsyncDisposable
{
    // ── Fields ────────────────────────────────────────────────────────────────

    private AppDbContext? _readContext;
    private SqliteConnection? _connection;

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_readContext is not null)
            await _readContext.DisposeAsync();

        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a fully-wired <see cref="AuditRepository"/> backed by a real
    /// SQLite database.  The write path uses a mock factory that always returns
    /// a fresh context on the same in-memory connection so writes are visible
    /// to the read context.
    /// </summary>
    private async Task<(AuditRepository repo, AppDbContext readCtx)> CreateRepositoryAsync()
    {
        (var read, var write, _connection) =
            await AuditTestDbContextFactory.CreatePairAsync();

        _readContext = read;

        // The factory mock returns the pre-created write context (same SQLite
        // database as the read context) so that INSERT visibility is immediate.
        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(write);

        var repo = new AuditRepository(
            contextFactory: factoryMock.Object,
            readContext: read,
            logger: NullLogger<AuditRepository>.Instance);

        return (repo, read);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Happy-path tests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InsertAsync_WhenValidUserEntry_PersistsEntryToDatabase()
    {
        // Arrange
        (var repo, var readCtx) = await CreateRepositoryAsync();

        var userId = Guid.NewGuid();
        var entry = AuditLogEntryBuilder.ALogin()
            .WithUserId(userId)
            .WithTimestampUtc(DateTime.UtcNow)
            .WithIpAddress("192.168.1.100")
            .Build();

        // Act
        await repo.InsertAsync(entry, CancellationToken.None);

        // Assert — verify the record was committed to the database
        var persisted = await readCtx.AuditLogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entry.Id);

        persisted.Should().NotBeNull();
        persisted!.UserId.Should().Be(userId);
        persisted.ActionType.Should().Be(AuditActionType.Login);
        persisted.IpAddress.Should().Be("192.168.1.100");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InsertAsync_WhenValidSystemEntry_PersistsEntryWithEmptyUserId()
    {
        // Arrange
        (var repo, var readCtx) = await CreateRepositoryAsync();

        var entry = new AuditLogEntryBuilder()
            .WithActionType(AuditActionType.AccountLocked)
            .WithTimestampUtc(DateTime.UtcNow)
            .AsSystemEntry()
            .Build();

        // Act
        await repo.InsertAsync(entry, CancellationToken.None);

        // Assert
        var persisted = await readCtx.AuditLogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entry.Id);

        persisted.Should().NotBeNull();
        persisted!.UserId.Should().Be(Guid.Empty);
        persisted.ActionType.Should().Be(AuditActionType.AccountLocked);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InsertAsync_WhenSearchEntry_PersistsAllOptionalFields()
    {
        // Arrange
        (var repo, var readCtx) = await CreateRepositoryAsync();

        var userId = Guid.NewGuid();
        var entry = AuditLogEntryBuilder.ASearch()
            .WithUserId(userId)
            .WithTimestampUtc(DateTime.UtcNow)
            .WithResultCount(15)
            .WithProfileName("my-profile")
            .WithParameters("{\"filter\":\"active\"}")
            .Build();

        // Act
        await repo.InsertAsync(entry, CancellationToken.None);

        // Assert
        var persisted = await readCtx.AuditLogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entry.Id);

        persisted.Should().NotBeNull();
        persisted!.ResultCount.Should().Be(15);
        persisted.ProfileName.Should().Be("my-profile");
        persisted.Parameters.Should().Be("{\"filter\":\"active\"}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InsertAsync_WhenViewTransactionEntry_PersistsS3ObjectKey()
    {
        // Arrange
        (var repo, var readCtx) = await CreateRepositoryAsync();

        const string s3Key = "2024/06/01/txn-abc123.gz";
        var entry = AuditLogEntryBuilder.AViewTransaction()
            .WithUserId(Guid.NewGuid())
            .WithTimestampUtc(DateTime.UtcNow)
            .WithS3ObjectKey(s3Key)
            .Build();

        // Act
        await repo.InsertAsync(entry, CancellationToken.None);

        // Assert
        var persisted = await readCtx.AuditLogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entry.Id);

        persisted.Should().NotBeNull();
        persisted!.S3ObjectKey.Should().Be(s3Key);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InsertAsync_WhenMultipleEntries_PersistsAllIndependently()
    {
        // Arrange
        (var repo, _) = await CreateRepositoryAsync();
        var userId = Guid.NewGuid();

        var entry1 = AuditLogEntryBuilder.ALogin()
            .WithUserId(userId)
            .WithTimestampUtc(DateTime.UtcNow)
            .Build();

        var entry2 = AuditLogEntryBuilder.ASearch()
            .WithUserId(userId)
            .WithTimestampUtc(DateTime.UtcNow.AddSeconds(1))
            .Build();

        // Act
        await repo.InsertAsync(entry1, CancellationToken.None);
        await repo.InsertAsync(entry2, CancellationToken.None);

        // Assert
        var count = await _readContext!.AuditLogEntries
            .AsNoTracking()
            .CountAsync(e => e.UserId == userId);

        count.Should().Be(2);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Timestamp enforcement tests (AC: TimestampUtc enforcement)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InsertAsync_WhenTimestampIsMinValue_ThrowsAuditFailureException()
    {
        // Arrange
        (var repo, _) = await CreateRepositoryAsync();

        // Build an entry with a manually crafted timestamp that equals DateTime.MinValue.
        // The only way to achieve this is to pass it explicitly to the factory method.
        var entry = AuditLogEntry.CreateForUser(
            userId: Guid.NewGuid(),
            actionType: AuditActionType.Login,
            timestampUtc: DateTime.MinValue); // ← the problematic value

        // Act
        var act = () => repo.InsertAsync(entry, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<AuditFailureException>()
            .WithMessage("*DateTime.MinValue*");
    }

    [Fact]
    public async Task InsertAsync_WhenTimestampIsMinValue_AuditedActionMatchesEntryActionType()
    {
        // Arrange
        (var repo, _) = await CreateRepositoryAsync();

        var entry = AuditLogEntry.CreateForUser(
            userId: Guid.NewGuid(),
            actionType: AuditActionType.SearchTransactions,
            timestampUtc: DateTime.MinValue);

        // Act
        var act = () => repo.InsertAsync(entry, CancellationToken.None);

        // Assert — AuditedAction should reflect the action that was being audited
        await act.Should().ThrowAsync<AuditFailureException>()
            .Where(ex => ex.AuditedAction == AuditActionType.SearchTransactions.ToString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InsertAsync_WhenTimestampIsValidUtcNow_PersistsWithoutModification()
    {
        // Arrange
        (var repo, var readCtx) = await CreateRepositoryAsync();

        var timestamp = new DateTime(2024, 3, 15, 10, 30, 0, DateTimeKind.Utc);
        var entry = AuditLogEntry.CreateForUser(
            userId: Guid.NewGuid(),
            actionType: AuditActionType.Login,
            timestampUtc: timestamp);

        // Act
        await repo.InsertAsync(entry, CancellationToken.None);

        // Assert — timestamp value is preserved
        var persisted = await readCtx.AuditLogEntries
            .AsNoTracking()
            .FirstAsync(e => e.Id == entry.Id);

        persisted.TimestampUtc.Should().BeCloseTo(timestamp, TimeSpan.FromSeconds(1));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Null-guard tests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InsertAsync_WhenEntryIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        (var repo, _) = await CreateRepositoryAsync();

        // Act
        var act = () => repo.InsertAsync(null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("entry");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Exception propagation tests (AC: No exception swallowed)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InsertAsync_WhenDbUpdateExceptionThrown_WrapsAsAuditFailureException()
    {
        // Arrange — factory mock throws DbUpdateException to simulate a constraint violation
        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        var dbUpdateEx = new DbUpdateException("Unique constraint violation");

        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(dbUpdateEx);

        (_readContext, _connection) = await AuditTestDbContextFactory.CreateAsync();

        var repo = new AuditRepository(
            contextFactory: factoryMock.Object,
            readContext: _readContext,
            logger: NullLogger<AuditRepository>.Instance);

        var entry = AuditLogEntryBuilder.ALogin()
            .WithTimestampUtc(DateTime.UtcNow)
            .Build();

        // Act
        var act = () => repo.InsertAsync(entry, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<AuditFailureException>()
            .Where(ex => ex.InnerException is DbUpdateException);
    }

    [Fact]
    public async Task InsertAsync_WhenDbUpdateExceptionThrown_AuditedActionContainsActionType()
    {
        // Arrange
        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("connection error"));

        (_readContext, _connection) = await AuditTestDbContextFactory.CreateAsync();

        var repo = new AuditRepository(
            contextFactory: factoryMock.Object,
            readContext: _readContext,
            logger: NullLogger<AuditRepository>.Instance);

        var entry = AuditLogEntryBuilder.ASearch()
            .WithTimestampUtc(DateTime.UtcNow)
            .Build();

        // Act
        var act = () => repo.InsertAsync(entry, CancellationToken.None);

        // Assert — AuditedAction reflects the action type from the entry
        await act.Should().ThrowAsync<AuditFailureException>()
            .Where(ex => ex.AuditedAction == AuditActionType.SearchTransactions.ToString());
    }

    [Fact]
    public async Task InsertAsync_WhenGenericExceptionThrown_WrapsAsAuditFailureException()
    {
        // Arrange — simulate an unexpected provider-level failure (e.g. startup error)
        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Provider unavailable"));

        (_readContext, _connection) = await AuditTestDbContextFactory.CreateAsync();

        var repo = new AuditRepository(
            contextFactory: factoryMock.Object,
            readContext: _readContext,
            logger: NullLogger<AuditRepository>.Instance);

        var entry = AuditLogEntryBuilder.ALogin()
            .WithTimestampUtc(DateTime.UtcNow)
            .Build();

        // Act
        var act = () => repo.InsertAsync(entry, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<AuditFailureException>()
            .Where(ex => ex.InnerException is InvalidOperationException);
    }

    [Fact]
    public async Task InsertAsync_WhenGenericExceptionThrown_MessageContainsOriginalMessage()
    {
        // Arrange
        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        const string innerMessage = "Provider unavailable";
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(innerMessage));

        (_readContext, _connection) = await AuditTestDbContextFactory.CreateAsync();

        var repo = new AuditRepository(
            contextFactory: factoryMock.Object,
            readContext: _readContext,
            logger: NullLogger<AuditRepository>.Instance);

        var entry = AuditLogEntryBuilder.ALogin()
            .WithTimestampUtc(DateTime.UtcNow)
            .Build();

        // Act
        var act = () => repo.InsertAsync(entry, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<AuditFailureException>()
            .WithMessage($"*{innerMessage}*");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Cancellation tests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InsertAsync_WhenCancellationRequestedBeforeFactoryCall_ThrowsOperationCanceledException()
    {
        // Arrange
        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        (_readContext, _connection) = await AuditTestDbContextFactory.CreateAsync();

        var repo = new AuditRepository(
            contextFactory: factoryMock.Object,
            readContext: _readContext,
            logger: NullLogger<AuditRepository>.Instance);

        var entry = AuditLogEntryBuilder.ALogin()
            .WithTimestampUtc(DateTime.UtcNow)
            .Build();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancel

        // Act
        var act = () => repo.InsertAsync(entry, cts.Token);

        // Assert — OperationCanceledException is NOT wrapped; it propagates as-is
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InsertAsync_WhenCancelled_DoesNotThrowAuditFailureException()
    {
        // Arrange
        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        (_readContext, _connection) = await AuditTestDbContextFactory.CreateAsync();

        var repo = new AuditRepository(
            contextFactory: factoryMock.Object,
            readContext: _readContext,
            logger: NullLogger<AuditRepository>.Instance);

        var entry = AuditLogEntryBuilder.ALogin()
            .WithTimestampUtc(DateTime.UtcNow)
            .Build();

        // Act
        var act = () => repo.InsertAsync(entry, CancellationToken.None);

        // Assert — must NOT throw AuditFailureException for cancellation
        await act.Should().ThrowAsync<OperationCanceledException>();
        await act.Should().NotThrowAsync<AuditFailureException>();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Independent DbContext scope tests (AC: dedicated IDbContextFactory scope)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InsertAsync_AlwaysCreatesNewContextViaFactory_NotUsingReadContext()
    {
        // Arrange — verify the factory is called exactly once per InsertAsync invocation
        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();

        (_readContext, var writeCtx, _connection) =
            await AuditTestDbContextFactory.CreatePairAsync();

        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(writeCtx);

        var repo = new AuditRepository(
            contextFactory: factoryMock.Object,
            readContext: _readContext,
            logger: NullLogger<AuditRepository>.Instance);

        var entry = AuditLogEntryBuilder.ALogin()
            .WithTimestampUtc(DateTime.UtcNow)
            .Build();

        // Act
        await repo.InsertAsync(entry, CancellationToken.None);

        // Assert — factory was invoked once (i.e. a fresh context was created for the write)
        factoryMock.Verify(
            f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InsertAsync_CalledTwice_CreatesFactoryContextTwice()
    {
        // Arrange
        (_readContext, _connection) = await AuditTestDbContextFactory.CreateAsync();

        // The factory must return a new independent context each time
        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => AuditTestDbContextFactory
                .CreateAsync()
                .GetAwaiter()
                .GetResult()
                .context);

        var repo = new AuditRepository(
            contextFactory: factoryMock.Object,
            readContext: _readContext,
            logger: NullLogger<AuditRepository>.Instance);

        var entry1 = AuditLogEntryBuilder.ALogin().WithTimestampUtc(DateTime.UtcNow).Build();
        var entry2 = AuditLogEntryBuilder.ASearch().WithTimestampUtc(DateTime.UtcNow).Build();

        // Act
        await repo.InsertAsync(entry1, CancellationToken.None);
        await repo.InsertAsync(entry2, CancellationToken.None);

        // Assert — factory invoked once per insert
        factoryMock.Verify(
            f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // IAuditRepository interface contract (append-only surface)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AuditRepository_ImplementsIAuditRepositoryInterface()
    {
        // Assert — compile-time + runtime verification that the class implements the interface
        typeof(AuditRepository).Should().Implement<IAuditRepository>();
    }

    [Fact]
    public void IAuditRepository_DoesNotExposeUpdateDeleteOrGetById()
    {
        // Arrange
        var interfaceType = typeof(IAuditRepository);
        var methodNames = interfaceType.GetMethods().Select(m => m.Name).ToArray();

        // Assert — only InsertAsync and GetPagedAsync are present
        methodNames.Should().Contain(nameof(IAuditRepository.InsertAsync));
        methodNames.Should().Contain(nameof(IAuditRepository.GetPagedAsync));
        methodNames.Should().NotContain("UpdateAsync");
        methodNames.Should().NotContain("DeleteAsync");
        methodNames.Should().NotContain("GetByIdAsync");
        methodNames.Should().HaveCount(2,
            because: "IAuditRepository must only expose InsertAsync and GetPagedAsync (append-only contract)");
    }
}
