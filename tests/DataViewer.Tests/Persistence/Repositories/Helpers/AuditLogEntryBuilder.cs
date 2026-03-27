using DataViewer.Domain.Entities;
using DataViewer.Domain.Enums;

namespace DataViewer.Tests.Persistence.Repositories.Helpers;

/// <summary>
/// Fluent test-data builder for <see cref="AuditLogEntry"/> instances.
/// </summary>
/// <remarks>
/// Delegates to the entity's own factory methods (<see cref="AuditLogEntry.CreateForUser"/>
/// and <see cref="AuditLogEntry.CreateForSystem"/>) to guarantee all domain invariants
/// are satisfied, keeping tests concise and focused on the scenario under test.
/// </remarks>
public sealed class AuditLogEntryBuilder
{
    private Guid _userId = Guid.NewGuid();
    private AuditActionType _actionType = AuditActionType.Login;
    private DateTime _timestampUtc = DateTime.UtcNow;
    private string? _ipAddress = "127.0.0.1";
    private string? _parameters = null;
    private int? _resultCount = null;
    private string? _s3ObjectKey = null;
    private string? _profileName = null;
    private bool _isSystem = false;

    // ── Fluent setters ────────────────────────────────────────────────────────

    public AuditLogEntryBuilder WithUserId(Guid userId)
    {
        _userId = userId;
        return this;
    }

    public AuditLogEntryBuilder WithActionType(AuditActionType actionType)
    {
        _actionType = actionType;
        return this;
    }

    public AuditLogEntryBuilder WithTimestampUtc(DateTime timestampUtc)
    {
        _timestampUtc = timestampUtc;
        return this;
    }

    public AuditLogEntryBuilder WithIpAddress(string? ipAddress)
    {
        _ipAddress = ipAddress;
        return this;
    }

    public AuditLogEntryBuilder WithParameters(string? parameters)
    {
        _parameters = parameters;
        return this;
    }

    public AuditLogEntryBuilder WithResultCount(int? resultCount)
    {
        _resultCount = resultCount;
        return this;
    }

    public AuditLogEntryBuilder WithS3ObjectKey(string? s3ObjectKey)
    {
        _s3ObjectKey = s3ObjectKey;
        return this;
    }

    public AuditLogEntryBuilder WithProfileName(string? profileName)
    {
        _profileName = profileName;
        return this;
    }

    public AuditLogEntryBuilder AsSystemEntry()
    {
        _isSystem = true;
        return this;
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Produces an <see cref="AuditLogEntry"/> using the appropriate factory method
    /// based on whether <see cref="AsSystemEntry"/> was called.
    /// </summary>
    public AuditLogEntry Build()
    {
        if (_isSystem)
        {
            return AuditLogEntry.CreateForSystem(
                actionType: _actionType,
                timestampUtc: _timestampUtc,
                parameters: _parameters);
        }

        return AuditLogEntry.CreateForUser(
            userId: _userId,
            actionType: _actionType,
            timestampUtc: _timestampUtc,
            ipAddress: _ipAddress,
            parameters: _parameters,
            resultCount: _resultCount,
            s3ObjectKey: _s3ObjectKey,
            profileName: _profileName);
    }

    // ── Convenience factory methods ───────────────────────────────────────────

    /// <summary>Returns a builder pre-configured for a <see cref="AuditActionType.Login"/> entry.</summary>
    public static AuditLogEntryBuilder ALogin() =>
        new AuditLogEntryBuilder().WithActionType(AuditActionType.Login);

    /// <summary>Returns a builder pre-configured for a <see cref="AuditActionType.SearchTransactions"/> entry.</summary>
    public static AuditLogEntryBuilder ASearch() =>
        new AuditLogEntryBuilder()
            .WithActionType(AuditActionType.SearchTransactions)
            .WithResultCount(42)
            .WithProfileName("prod-profile")
            .WithParameters("{\"query\":\"test\"}");

    /// <summary>Returns a builder pre-configured for a <see cref="AuditActionType.ViewTransaction"/> entry.</summary>
    public static AuditLogEntryBuilder AViewTransaction() =>
        new AuditLogEntryBuilder()
            .WithActionType(AuditActionType.ViewTransaction)
            .WithS3ObjectKey("2024/01/01/txn-001.gz")
            .WithProfileName("prod-profile");
}
